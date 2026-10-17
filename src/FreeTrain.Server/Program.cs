var builder = WebApplication.CreateBuilder(args);
// Loopback only unless an explicit deployment URL is configured.
if (string.IsNullOrEmpty(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://127.0.0.1:5080");
var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok", stage = "foundation", protocolVersion = 1 }));
// Gameplay endpoints arrive in Stage 4; health does not imply a running simulation.
app.Run();
