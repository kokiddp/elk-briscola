var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/", () => Results.Ok("elk-briscola api"));

app.Run();

public partial class Program;
