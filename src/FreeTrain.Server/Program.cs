using FreeTrain.Server;
using FreeTrain.Persistence;

if (args.Length > 0 && args[0] == "--headless-trace")
    return HeadlessTrace.Run(args);

var builder = WebApplication.CreateBuilder(args);
// Loopback only unless an explicit deployment URL is configured.
if (string.IsNullOrEmpty(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://127.0.0.1:5080");
if (string.IsNullOrEmpty(builder.Configuration["FreeTrain:OperatorSecret"]) &&
    builder.Configuration["urls"]!.Split(';').Any(url => !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsLoopback))
    throw new InvalidOperationException("A non-loopback binding requires FreeTrain__OperatorSecret.");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = SaveCodec.MaximumBytes);
var networkOptions = builder.Configuration.GetSection("FreeTrain:Network").Get<NetworkOptions>() ?? new();
networkOptions.Validate();
var checkpointOptions = builder.Configuration.GetSection("FreeTrain:Saves").Get<CheckpointOptions>() ?? new();
if (checkpointOptions.Interval < TimeSpan.FromSeconds(1) || checkpointOptions.Interval > TimeSpan.FromMinutes(5))
    throw new InvalidOperationException("Checkpoint interval must be between one second and five minutes.");
builder.Services.AddSingleton(checkpointOptions);
builder.Services.AddSingleton(_ => new SaveStorage(checkpointOptions.Directory));
builder.Services.AddSingleton(networkOptions);
builder.Services.AddSingleton<NetworkSession>();
builder.Services.AddSingleton<SessionDriver>();
builder.Services.AddHostedService(services => services.GetRequiredService<SessionDriver>());
var app = builder.Build();
app.MapGet("/health", (SessionDriver driver) => Results.Ok(new { status = driver.CheckpointError == null ? "ok" : "degraded", stage = "persistence", protocolVersion = 1, checkpointError = driver.CheckpointError }));
app.MapNetwork();
app.Run();

return 0;
