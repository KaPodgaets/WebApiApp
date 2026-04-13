using ModelContextProtocol.Protocol;
using WebApiApp.Mcp;

EnvFileLoader.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(new ClientAppInfo(
    builder.Configuration["CLIENT_APP_ID"] ??
    Environment.GetEnvironmentVariable("CLIENT_APP_ID") ??
    string.Empty));
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "web-api-app",
            Version = "1.0.0"
        };
        options.ServerInstructions =
            "Use the exposed tools for single-digit addition, single-digit multiplication, and reading the current UTC date and time.";
    })
    .WithTools<WebApiMcpTools>()
    .WithHttpTransport()
    .AddAuthorizationFilters();

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
    status = "ok",
    mcp = new
    {
        endpoint = "/mcp",
        transport = "streamable-http",
        tools = new[]
        {
            "sum_digits",
            "multiply_digits",
            "get_utc_datetime",
            "get_client_app_id"
        }
    }
}));

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapControllers();
app.MapMcp("/mcp");

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

static class EnvFileLoader
{
    public static void Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        foreach (var rawLine in File.ReadAllLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"');

            if (!string.IsNullOrWhiteSpace(key) &&
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
