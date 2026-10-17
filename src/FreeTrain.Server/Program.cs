using FreeTrain.Server;

if (args.Length > 0 && args[0] == "--headless-trace")
    return HeadlessTrace.Run(args);

var builder = WebApplication.CreateBuilder(args);
// Loopback only unless an explicit deployment URL is configured.
if (string.IsNullOrEmpty(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://127.0.0.1:5080");
builder.Services.AddSingleton(SessionComposition.CreateReferenceHost());
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok", stage = "headless", protocolVersion = 1 }));
// The scenario starts paused. Gameplay endpoints and the service tick driver arrive in Stage 4.
app.Run();

return 0;
