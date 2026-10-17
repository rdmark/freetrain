using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FreeTrain.Contracts;
using FreeTrain.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class NetworkTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static CommandMessage Command(string id, CommandPayload payload, long epoch = 1) => new(id, epoch, payload);
    private static async Task<WireMessage> Apply(NetworkSession session, NetworkSession.Peer peer, CommandMessage command, DateTimeOffset? now = null)
    {
        var task = session.Submit(peer, command, now ?? Now);
        session.Tick(now ?? Now);
        return await task;
    }
    [Fact]
    public async Task Receipts_share_pending_and_committed_outcomes_and_do_not_charge_twice()
    {
        var session = new NetworkSession(new());
        var peer = session.Connect(Now)!;
        var command = Command("build", new BuildTrackCommand(new(37, 16, 1), "buffer"));
        var pending = session.Submit(peer, command, Now);
        Assert.False(pending.IsCompleted);
        Assert.Same(pending, session.Submit(peer, command, Now));
        session.Tick(Now);
        var accepted = Assert.IsType<AcceptedMessage>(await pending);
        Assert.Equal(1, accepted.Sequence);
        var cash = session.Bootstrap().Snapshot.Cash;
        Assert.True(cash < 1000000);
        Assert.Equal(accepted, await session.Submit(peer, command, Now.AddSeconds(10)));
        Assert.Equal(cash, session.Bootstrap().Snapshot.Cash);
        Assert.Equal("requestConflict", Assert.IsType<RejectedMessage>(await session.Submit(peer, command with { Payload = new StepCommand() }, Now)).Code);
        session.Disconnect(peer, Now);
        var next = session.Connect(Now)!;
        Assert.Equal(accepted, await session.Submit(next, command, Now.AddSeconds(11)));
    }
    [Fact]
    public async Task Receipt_capacity_rejects_new_ids_without_evicting_retry_guarantees()
    {
        var session = new NetworkSession(new() { ReceiptCapacity = 1 });
        var peer = session.Connect(Now)!;
        var command = Command("step", new StepCommand());
        var first = await Apply(session, peer, command);
        Assert.Equal("retryWindowFull", Assert.IsType<RejectedMessage>(await session.Submit(peer, Command("other", new StepCommand()), Now)).Code);
        Assert.Equal(first, await session.Submit(peer, command, Now));
        // Expiry is explicit: a fresh ID becomes available after two minutes.
        Assert.IsType<AcceptedMessage>(await Apply(session, peer, Command("fresh", new StepCommand()), Now.AddMinutes(3)));
    }
    [Fact]
    public async Task Stale_epochs_and_entity_edits_preserve_state_and_rejections_are_deduplicated()
    {
        var session = new NetworkSession(new()); var peer = session.Connect(Now)!;
        var initial = session.Bootstrap().Snapshot;
        var stale = Command("stale", new StepCommand(), 2);
        Assert.Equal("staleEpoch", Assert.IsType<RejectedMessage>(await Apply(session, peer, stale)).Code);
        var edit = Command("edit", new RenameStationCommand("station-1", 99, "新宿駅"));
        var rejected = await Apply(session, peer, edit);
        Assert.Equal("staleRevision", Assert.IsType<RejectedMessage>(rejected).Code);
        Assert.Equal(rejected, await session.Submit(peer, edit, Now));
        Assert.Equal(initial.Revision, session.Bootstrap().Snapshot.Revision);
    }
    [Fact]
    public async Task Wall_time_input_expiry_works_while_paused_and_disconnect_grace_pauses_running_game()
    {
        var session = new NetworkSession(new()); var peer = session.Connect(Now)!;
        await Apply(session, peer, Command("manual", new SetControllerCommand("train-1", 0, "manual")));
        await Apply(session, peer, Command("input", new ControllerInputCommand("train-1", 1, false, true, false, false, false, false)));
        var before = session.Bootstrap().Snapshot;
        session.Tick(Now.AddMilliseconds(600));
        var expired = session.Bootstrap().Snapshot;
        Assert.Equal(0, expired.Tick);
        Assert.True(expired.Trains[0].Revision > before.Trains[0].Revision);
        await Apply(session, peer, Command("run", new RunCommand()), Now.AddMilliseconds(650));
        session.Disconnect(peer, Now.AddMilliseconds(700));
        session.Tick(Now.AddMilliseconds(800)); Assert.False(session.Bootstrap().Snapshot.Paused);
        session.Tick(Now.AddSeconds(2)); Assert.True(session.Bootstrap().Snapshot.Paused);
        var next = session.Connect(Now.AddSeconds(2))!;
        Assert.Null(session.Connect(Now.AddSeconds(2)));
        Assert.True(session.Bootstrap().Snapshot.Paused);
        session.Disconnect(next, Now.AddSeconds(3));
        session.Tick(Now.AddMinutes(6));
        Assert.Equal(2, session.Bootstrap().WorldEpoch);
        Assert.Equal(0, session.Bootstrap().Snapshot.Tick);
        Assert.True(session.Bootstrap().Snapshot.Paused);
    }
    [Fact]
    public async Task Slow_output_is_disconnected_without_blocking_host_or_dropping_dependent_deltas()
    {
        var session = new NetworkSession(new() { OutputCapacity = 2 }); var peer = session.Connect(Now)!;
        await Apply(session, peer, Command("run", new RunCommand()));
        session.Tick(Now.AddMilliseconds(100));
        Assert.True(peer.Stop.IsCancellationRequested);
        Assert.True(session.Bootstrap().Snapshot.Tick >= 2);
        var recovered = session.Connect(Now.AddMilliseconds(200));
        Assert.NotNull(recovered);
        Assert.IsType<JoinMessage>(await recovered!.Output.Reader.ReadAsync());
    }
    [Fact]
    public void Idle_socket_is_released_and_deltas_include_scalars_tracks_and_all_removals()
    {
        var session = new NetworkSession(new()); var peer = session.Connect(Now)!;
        session.Tick(Now.AddSeconds(16)); Assert.True(peer.Stop.IsCancellationRequested);
        var old = session.Bootstrap().Snapshot;
        var next = old with { Revision = old.Revision + 1, Tick = 1, Cash = 9007199254740993L, Paused = false,
            Tracks = old.Tracks.Skip(1).ToArray(), Buildings = [], Stations = [], Trains = [] };
        var delta = NetworkSession.Difference(old, next);
        Assert.Equal(old.Revision, delta.BaseRevision); Assert.False(delta.Paused);
        Assert.Equal(next.Cash, delta.Cash); Assert.Single(delta.Removed.TrackPositions);
        Assert.Equal(old.Trains.Select(t => t.Id), delta.Removed.TrainIds);
        Assert.Equal(old.Buildings.Select(t => t.Id), delta.Removed.BuildingIds);
        Assert.Equal(old.Stations.Select(t => t.Id), delta.Removed.StationIds);
        Assert.Empty(delta.Tracks);
    }
    [Fact]
    public async Task Publication_slots_preserve_rate_under_timer_jitter_without_catch_up_bursts()
    {
        var started = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var session = new NetworkSession(new()); var peer = session.Connect(started)!;
        await Apply(session, peer, Command("run", new RunCommand()), started);
        while (peer.Output.Reader.TryRead(out _)) { }
        var publications = 0;
        for (var index = 1; index <= 20; index++)
        {
            session.Tick(started.AddMilliseconds(index * 50 - (index % 3 == 0 ? 0 : 1)));
            while (peer.Output.Reader.TryRead(out var message)) if (message is DeltaMessage) publications++;
        }
        Assert.Equal(9, publications); // 100..900ms slots; first publication was at 0ms.
        session.Tick(started.AddSeconds(3));
        Assert.True(peer.Output.Reader.TryRead(out var afterStall)); Assert.IsType<DeltaMessage>(afterStall);
        Assert.False(peer.Output.Reader.TryRead(out _));
    }
    [Fact]
    public async Task Real_http_and_socket_enforce_access_versions_origins_and_exclusive_owner()
    {
        await using var server = await Harness.Start("test-operator-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Http.GetAsync(Harness.BootstrapPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.Http.PostAsJsonAsync("/api/access", new { secret = "wrong" })).StatusCode);
        using var crossSite = new HttpRequestMessage(HttpMethod.Post, "/api/access") { Content = JsonContent.Create(new { secret = "test-operator-secret" }) };
        crossSite.Headers.Add("Origin", "https://untrusted.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await server.Http.SendAsync(crossSite)).StatusCode);
        var login = await server.Http.PostAsJsonAsync("/api/access", new { secret = "test-operator-secret" });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie); Assert.Contains("samesite=strict", cookie);
        Assert.Equal((HttpStatusCode)426, (await server.Http.GetAsync("/api/bootstrap?protocolVersion=2&contentVersion=prototype-content-1")).StatusCode);
        Assert.Equal((HttpStatusCode)426, (await server.Http.GetAsync("/api/bootstrap?protocolVersion=1&contentVersion=bad")).StatusCode);
        var join = await server.Bootstrap(); Assert.Equal("operator-1", join.ParticipantId);
        using var socket = await server.Connect(); Assert.IsType<JoinMessage>(await Read(socket));
        using var competitor = new ClientWebSocket(); competitor.Options.Cookies = server.Cookies;
        await Assert.ThrowsAsync<WebSocketException>(() => competitor.ConnectAsync(server.SocketUri, CancellationToken.None));
        using var unauthenticated = new ClientWebSocket();
        await Assert.ThrowsAsync<WebSocketException>(() => unauthenticated.ConnectAsync(server.SocketUri, CancellationToken.None));
        using var evil = new ClientWebSocket(); evil.Options.Cookies = server.Cookies; evil.Options.SetRequestHeader("Origin", "https://untrusted.invalid");
        await Assert.ThrowsAsync<WebSocketException>(() => evil.ConnectAsync(server.SocketUri, CancellationToken.None));
    }
    [Fact]
    public async Task Real_socket_commits_before_ack_retries_and_reconnects_to_confirmed_snapshot()
    {
        await using var server = await Harness.Start(); await server.Bootstrap();
        using var socket = await server.Connect(); var join = Assert.IsType<JoinMessage>(await Read(socket));
        var step = Command("retry", new StepCommand());
        await Send(socket, step);
        var delta = Assert.IsType<DeltaMessage>(await Read(socket));
        Assert.Equal(1, delta.Tick); Assert.Equal(join.Snapshot.Revision, delta.BaseRevision);
        var accepted = Assert.IsType<AcceptedMessage>(await Read(socket)); Assert.Equal(delta.Revision, accepted.Revision);
        await Send(socket, step); Assert.Equal(accepted, await Read(socket));
        await Send(socket, Command("stale", new StepCommand(), 99));
        Assert.Equal("staleEpoch", Assert.IsType<RejectedMessage>(await Read(socket)).Code);
        await Send(socket, Command("rename", new RenameStationCommand("station-1", 0, "東京駅")));
        Assert.Equal("東京駅", Assert.IsType<DeltaMessage>(await Read(socket)).Stations[0].Name);
        Assert.IsType<AcceptedMessage>(await Read(socket));
        await Send(socket, new ResyncMessage()); Assert.Equal(1, Assert.IsType<SnapshotMessage>(await Read(socket)).Tick);
        socket.Abort();
        using var recovered = await server.ConnectEventually();
        var fresh = Assert.IsType<JoinMessage>(await Read(recovered));
        Assert.Equal("東京駅", fresh.Snapshot.Stations[0].Name); Assert.Equal(1, fresh.Snapshot.Tick);
        await Send(recovered, step); Assert.Equal(accepted, await Read(recovered));
        await Send(recovered, Command("run", new RunCommand()));
        await Until(recovered, x => x is AcceptedMessage a && a.RequestId == "run");
        await Send(recovered, new InputResetMessage(true));
        var paused = Assert.IsType<DeltaMessage>(await Until(recovered, x => x is DeltaMessage d && d.Paused));
        Assert.True(paused.Paused);
    }
    [Fact]
    public async Task Fragmented_delayed_sender_does_not_freeze_clock_or_publication()
    {
        await using var server = await Harness.Start(); await server.Bootstrap();
        using var socket = await server.Connect(); await Read(socket);
        await Send(socket, Command("run", new RunCommand()));
        var accepted = Assert.IsType<AcceptedMessage>(await Until(socket, x => x is AcceptedMessage));
        // Leave a message unfinished while the real server continues ticking and publishing.
        var data = JsonSerializer.SerializeToUtf8Bytes<WireMessage>(Command("paused-step", new StepCommand()), NetworkSession.Json);
        await socket.SendAsync(new ArraySegment<byte>(data, 0, 10), WebSocketMessageType.Text, false, CancellationToken.None);
        var update = Assert.IsType<DeltaMessage>(await Until(socket, x => x is DeltaMessage d && d.Tick >= accepted.Tick + 2));
        Assert.True(update.Tick > accepted.Tick);
        await socket.SendAsync(new ArraySegment<byte>(data, 10, data.Length - 10), WebSocketMessageType.Text, true, CancellationToken.None);
        var rejected = Assert.IsType<RejectedMessage>(await Until(socket, x => x is RejectedMessage));
        Assert.Equal("invalidInput", rejected.Code);
        await Send(socket, new InputResetMessage(true));
        Assert.IsType<DeltaMessage>(await Until(socket, x => x is DeltaMessage d && d.Paused));
    }
    [Theory]
    [InlineData("{\"type\":\"command\",\"requestId\":\"bad\",\"expectedWorldEpoch\":\"1\",\"payload\":null}")]
    [InlineData("{\"type\":\"heartbeat\",\"extra\":true}")]
    [InlineData("oversized")]
    public async Task Malformed_or_oversized_messages_close_only_the_connection(string json)
    {
        await using var server = await Harness.Start(); await server.Bootstrap();
        using var socket = await server.Connect(); await Read(socket);
        var bytes = Encoding.UTF8.GetBytes(json == "oversized" ? new string('x', 16385) : json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(async () => await Read(socket));
        using var recovered = await server.ConnectEventually();
        Assert.Equal(0, Assert.IsType<JoinMessage>(await Read(recovered)).Snapshot.Tick);
    }
    private static Task Send(ClientWebSocket socket, WireMessage message) => socket.SendAsync(
        new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes<WireMessage>(message, NetworkSession.Json)), WebSocketMessageType.Text, true, CancellationToken.None);
    private static async Task<WireMessage> Read(ClientWebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var output = new MemoryStream(); var buffer = new byte[4096]; WebSocketReceiveResult frame;
        do
        {
            frame = await socket.ReceiveAsync(buffer, timeout.Token);
            if (frame.MessageType == WebSocketMessageType.Close) throw new InvalidDataException("Closed");
            output.Write(buffer, 0, frame.Count);
        } while (!frame.EndOfMessage);
        return JsonSerializer.Deserialize<WireMessage>(output.ToArray(), NetworkSession.Json)!;
    }
    private static async Task<WireMessage> Until(ClientWebSocket socket, Func<WireMessage, bool> predicate)
    {
        for (var count = 0; count < 40; count++) { var message = await Read(socket); if (predicate(message)) return message; }
        throw new InvalidOperationException("Expected message not received");
    }
    private sealed class Harness(WebApplication app, HttpClient http, CookieContainer cookies) : IAsyncDisposable
    {
        public const string BootstrapPath = "/api/bootstrap?protocolVersion=1&contentVersion=prototype-content-1";
        public HttpClient Http => http;
        public CookieContainer Cookies => cookies;
        public Uri SocketUri => new(new Uri(http.BaseAddress!.ToString().Replace("http:", "ws:")), "/api/session?protocolVersion=1&contentVersion=prototype-content-1");
        public static async Task<Harness> Start(string? secret = null)
        {
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["FreeTrain:OperatorSecret"] = secret });
            builder.Services.AddSingleton(new NetworkOptions()); builder.Services.AddSingleton<NetworkSession>(); builder.Services.AddHostedService<SessionDriver>();
            builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = NetworkEndpoints.MaximumMessageBytes);
            var app = builder.Build(); app.MapNetwork(); await app.StartAsync();
            var cookies = new CookieContainer();
            return new(app, new(new HttpClientHandler { CookieContainer = cookies }) { BaseAddress = new(app.Urls.Single()) }, cookies);
        }
        public async Task<JoinMessage> Bootstrap() => JsonSerializer.Deserialize<WireMessage>(await http.GetStringAsync(BootstrapPath), NetworkSession.Json) as JoinMessage ?? throw new InvalidDataException();
        public async Task<ClientWebSocket> Connect()
        {
            var socket = new ClientWebSocket(); socket.Options.Cookies = cookies;
            try { await socket.ConnectAsync(SocketUri, CancellationToken.None); return socket; } catch { socket.Dispose(); throw; }
        }
        public async Task<ClientWebSocket> ConnectEventually()
        {
            for (var count = 0; count < 50; count++) { try { return await Connect(); } catch (WebSocketException) { await Task.Delay(20); } }
            throw new InvalidOperationException("Lease never released");
        }
        public async ValueTask DisposeAsync() { http.Dispose(); await app.StopAsync(); await app.DisposeAsync(); }
    }
}
