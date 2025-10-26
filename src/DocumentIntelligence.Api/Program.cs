var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/health/live"));
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));

app.Run();

public partial class Program;
