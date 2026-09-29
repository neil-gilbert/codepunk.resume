var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ready" }));

// The upstream web compiler assumes one career record per host and exposes
// its current record through public GET endpoints. Do not mount it until
// candidate ownership and publication visibility are enforced per request.
app.Run();

public partial class Program;
