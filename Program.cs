var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/", () => Results.Ok(new
{
    application = app.Environment.ApplicationName,
    environment = app.Environment.EnvironmentName,
    status = "ok"
}));

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

var portValue = Environment.GetEnvironmentVariable("PORT");
var hasExplicitBinding =
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("URLS")) ||
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) ||
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_URLS")) ||
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HTTP_PORTS")) ||
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")) ||
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_HTTP_PORTS"));

// Azure App Service injects settings as environment variables. If the platform provides only PORT,
// bind Kestrel to it without overriding explicit URL/port configuration.
if (!hasExplicitBinding && int.TryParse(portValue, out var port))
{
    app.Urls.Add($"http://0.0.0.0:{port}");
}

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
