using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FreeTrain.Contracts;
using FreeTrain.Persistence;

namespace FreeTrain.Server;

public static class NetworkEndpoints
{
    private const string CookieName = "freetrain.operator";
    public const int MaximumMessageBytes = 16384;
    public static void MapNetwork(this WebApplication app)
    {
        var session = app.Services.GetRequiredService<NetworkSession>();
        app.Lifetime.ApplicationStopping.Register(session.PrepareShutdown);
        var secret = app.Configuration["FreeTrain:OperatorSecret"];
        if (string.IsNullOrEmpty(secret)) secret = null;
        var attempts = 0;
        var attemptWindow = DateTimeOffset.UtcNow;
        var accessGate = new object();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(5), KeepAliveTimeout = TimeSpan.FromSeconds(10) });
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api")) { await next(context); return; }
            context.Response.Headers.CacheControl = "no-store";
            if (!Permitted(context, app.Configuration, secret)) { context.Response.StatusCode = 403; return; }
            await next(context);
        });
        void Cookie(HttpContext context) => context.Response.Cookies.Append(CookieName, session.AccessCookie,
            new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps, Path = "/api", MaxAge = TimeSpan.FromHours(8) });
        bool Authorized(HttpContext context) => Equal(context.Request.Cookies[CookieName], session.AccessCookie);
        bool Compatible(HttpContext context) => context.Request.Query["protocolVersion"] == "1" && context.Request.Query["contentVersion"] == NetworkSession.ContentVersion;
        app.MapPost("/api/access", async (HttpContext context) =>
        {
            lock (accessGate)
            {
                if (DateTimeOffset.UtcNow - attemptWindow >= TimeSpan.FromMinutes(1)) { attemptWindow = DateTimeOffset.UtcNow; attempts = 0; }
                if (++attempts > 10) { context.Response.StatusCode = 429; return; }
            }
            if (secret == null) { context.Response.StatusCode = 404; return; }
            if (context.Request.ContentLength is > MaximumMessageBytes) { context.Response.StatusCode = 413; return; }
            try
            {
                var login = await JsonSerializer.DeserializeAsync<AccessRequest>(context.Request.Body, NetworkSession.Json, context.RequestAborted);
                if (login == null || !Equal(login.Secret, secret)) { context.Response.StatusCode = 401; return; }
                Cookie(context); context.Response.StatusCode = 204;
            }
            catch (JsonException) { context.Response.StatusCode = 400; }
        });
        app.MapGet("/api/bootstrap", (HttpContext context) =>
        {
            if (!Compatible(context)) return Results.Json(new { code = "incompatible", protocolVersion = 1, contentVersion = NetworkSession.ContentVersion }, statusCode: 426);
            if (!Authorized(context))
            {
                if (secret != null) return Results.Unauthorized();
                Cookie(context); // Anonymous access is limited to explicit loopback hosts.
            }
            return Results.Json<WireMessage>(session.Bootstrap(), NetworkSession.Json);
        });
        // Save HTTP requests use the same trusted owner credential and host FIFO as socket commands.
        app.MapGet("/api/saves", (HttpContext context) => Authorized(context)
            ? Results.Ok(session.SaveSlots()) : Results.Unauthorized());
        app.MapGet("/api/saves/download", async (HttpContext context) =>
        {
            if (!Authorized(context)) return Results.Unauthorized();
            try { return Results.File(SaveCodec.Encode(await session.CaptureSave()), "application/json", "freetrain-save.json"); }
            catch (InvalidOperationException) { return Results.Json(new { code = "queueFull" }, statusCode: 503); }
        });
        async Task<IResult> SaveOperation(HttpContext context, Func<long, Task> action)
        {
            if (!Authorized(context)) return Results.Unauthorized();
            if (!long.TryParse(context.Request.Query["expectedWorldEpoch"], out var epoch) || epoch < 0)
                return Results.BadRequest(new { code = "invalidInput" });
            try { await action(epoch); return Results.Ok(new { status = "confirmed" }); }
            catch (InvalidDataException ex) { return Results.Json(new { code = ex.Message == "saveTooLarge" ? "saveTooLarge" : "invalidSave" }, statusCode: ex.Message == "saveTooLarge" ? 413 : 400); }
            catch (FileNotFoundException) { return Results.NotFound(new { code = "missingSave" }); }
            catch (InvalidOperationException ex) { return Results.Json(new { code = ex.Message == "staleEpoch" ? "staleEpoch" : "queueFull" }, statusCode: 409); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return Results.Json(new { code = "saveFailed" }, statusCode: 503); }
        }
        app.MapPost("/api/saves/upload", async Task<IResult> (HttpContext context) => await SaveOperation(context, async epoch =>
        {
            if (context.Request.ContentLength is > SaveCodec.MaximumBytes) throw new InvalidDataException("saveTooLarge");
            using var buffer = new MemoryStream(); var chunk = new byte[8192]; int count;
            while ((count = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) > 0)
            {
                if (buffer.Length + count > SaveCodec.MaximumBytes) throw new InvalidDataException("saveTooLarge");
                buffer.Write(chunk, 0, count);
            }
            await session.LoadSave(SaveCodec.Decode(buffer.ToArray()), epoch);
        }));
        app.MapPost("/api/saves/{id}/save", (HttpContext context, string id) => SaveOperation(context, async epoch =>
        {
            if (session.Bootstrap().WorldEpoch != epoch) throw new InvalidOperationException("staleEpoch");
            if (!SaveStorage.Slots.Contains(id)) throw new InvalidDataException("invalidSlot");
            await session.Checkpoint(id, epoch);
        }));
        app.MapPost("/api/saves/{id}/load", (HttpContext context, string id) => SaveOperation(context, epoch => session.LoadSlot(id, epoch)));
        app.MapGet("/api/session", async (HttpContext context) =>
        {
            if (!Authorized(context)) { context.Response.StatusCode = 401; return; }
            if (!Compatible(context)) { context.Response.StatusCode = 426; return; }
            if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
            var peer = session.Connect(DateTimeOffset.UtcNow);
            if (peer == null) { context.Response.StatusCode = 409; return; }
            try
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, peer.Stop.Token);
                var sender = Send(socket, peer, linked.Token);
                var receiver = Receive(socket, peer, session, linked.Token);
                await Task.WhenAny(sender, receiver);
                linked.Cancel(); socket.Abort();
                try { await Task.WhenAll(sender, receiver); }
                catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or JsonException or InvalidDataException) { }
            }
            finally { session.Disconnect(peer, DateTimeOffset.UtcNow); }
        });
    }
    private sealed record AccessRequest(string Secret);
    private static bool Equal(string? left, string? right) => left != null && right != null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    private static bool Permitted(HttpContext context, IConfiguration configuration, string? secret)
    {
        if (secret == null && (!IPAddress.TryParse(context.Connection.RemoteIpAddress?.ToString(), out var ip) || !IPAddress.IsLoopback(ip)
            || !(context.Request.Host.Host is "127.0.0.1" or "localhost" or "[::1]" or "::1"))) return false;
        var origin = context.Request.Headers.Origin.ToString();
        if (context.Request.Headers["Sec-Fetch-Site"] == "cross-site") return false;
        if (origin.Length == 0) return true; // CLI clients still need the operator credential remotely.
        var own = $"{context.Request.Scheme}://{context.Request.Host}";
        var allowed = configuration.GetSection("FreeTrain:AllowedOrigins").Get<string[]>() ?? [];
        return origin == own || allowed.Contains(origin, StringComparer.Ordinal);
    }
    private static async Task Send(WebSocket socket, NetworkSession.Peer peer, CancellationToken cancellationToken)
    {
        await foreach (var message in peer.Output.Reader.ReadAllAsync(cancellationToken))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes<WireMessage>(message, NetworkSession.Json), WebSocketMessageType.Text, true, timeout.Token);
        }
    }
    private static async Task Receive(WebSocket socket, NetworkSession.Peer peer, NetworkSession session, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaximumMessageBytes + 1];
        while (!cancellationToken.IsCancellationRequested)
        {
            var length = 0;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            WebSocketReceiveResult frame;
            do
            {
                frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), timeout.Token);
                if (frame.MessageType == WebSocketMessageType.Close) return;
                if (frame.MessageType != WebSocketMessageType.Text || (length += frame.Count) > MaximumMessageBytes) throw new InvalidDataException("Message limit/type.");
            } while (!frame.EndOfMessage);
            if (!session.Touch(peer, DateTimeOffset.UtcNow)) return;
            var message = JsonSerializer.Deserialize<WireMessage>(buffer.AsSpan(0, length), NetworkSession.Json);
            switch (message)
            {
                case CommandMessage command when Valid(command):
                    var result = await session.Submit(peer, command, DateTimeOffset.UtcNow).WaitAsync(cancellationToken);
                    session.Reply(peer, result); break;
                case ResyncMessage: session.Resync(peer); break;
                case HeartbeatMessage: break;
                case InputResetMessage reset: session.ResetInput(peer, reset.Pause); break;
                default: throw new InvalidDataException("Unexpected message.");
            }
        }
    }
    private static bool Valid(CommandMessage command) => !string.IsNullOrWhiteSpace(command.RequestId) && command.RequestId.Length <= 128 && command.ExpectedWorldEpoch >= 0 && command.Payload switch
    {
        RenameStationCommand rename => rename.ExpectedRevision >= 0 && !string.IsNullOrWhiteSpace(rename.StationId) && rename.Name != null,
        RemoveTrackCommand remove => remove.ExpectedRevision >= 0 && remove.Position != null,
        BuildTrackCommand build => build.Position != null && build.Shape != null,
        SetControllerCommand controller => controller.ExpectedRevision >= 0 && !string.IsNullOrWhiteSpace(controller.TrainId) && controller.Controller is "manual" or "automatic",
        ControllerInputCommand input => input.InputSequence >= 0 && !string.IsNullOrWhiteSpace(input.TrainId),
        RunCommand or PauseCommand or StepCommand => true,
        _ => false
    };
}
