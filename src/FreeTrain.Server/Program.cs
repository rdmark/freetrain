using FreeTrain.Server;

if (args.Length > 0 && args[0] == "--headless-trace")
    return HeadlessTrace.Run(args);

var builder = WebApplication.CreateBuilder(args);
// Loopback only unless an explicit deployment URL is configured.
if (string.IsNullOrEmpty(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://127.0.0.1:5080");
if (string.IsNullOrEmpty(builder.Configuration["FreeTrain:OperatorSecret"]) &&
    builder.Configuration["urls"]!.Split(';').Any(url => !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsLoopback))
    throw new InvalidOperationException("A non-loopback binding requires FreeTrain__OperatorSecret.");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = NetworkEndpoints.MaximumMessageBytes);
var networkOptions = builder.Configuration.GetSection("FreeTrain:Network").Get<NetworkOptions>() ?? new();
networkOptions.Validate();
builder.Services.AddSingleton(networkOptions);
builder.Services.AddSingleton<NetworkSession>();
builder.Services.AddHostedService<SessionDriver>();
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok", stage = "network", protocolVersion = 1 }));
app.MapNetwork();
app.Run();

return 0;
