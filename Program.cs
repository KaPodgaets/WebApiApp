using Microsoft.AspNetCore.HttpLogging;
using ModelContextProtocol.Protocol;
using WebApiApp.EntraAuth;
using WebApiApp.Mcp;

var builder = WebApplication.CreateBuilder(args);
var entraSection = builder.Configuration.GetSection("EntraId");
var entraScopes = entraSection.GetSection("Scopes").Get<string[]>();
var defaultScope = entraScopes is { Length: > 0 }
    ? string.Join(' ', entraScopes)
    : "User.Read offline_access openid profile";
var dataDirectory = PathResolver.Resolve(
    builder.Configuration["ENTRA_DATA_DIRECTORY"] ??
    "data");
var authStateFilePath = PathResolver.Resolve(
    builder.Configuration["ENTRA_AUTH_STATE_FILE_PATH"] ??
    Path.Combine(dataDirectory, "entra-auth-state.json"));

Directory.CreateDirectory(dataDirectory);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddControllers();
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields =
        HttpLoggingFields.RequestMethod |
        HttpLoggingFields.RequestPath |
        HttpLoggingFields.ResponseStatusCode |
        HttpLoggingFields.Duration |
        HttpLoggingFields.RequestHeaders;
    options.RequestHeaders.Add("Mcp-Session-Id");
    options.RequestHeaders.Add("MCP-Protocol-Version");
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(new ClientAppInfo(
    entraSection["ClientId"] ??
    string.Empty));
builder.Services.AddSingleton(new EntraAuthOptions(
    entraSection["ClientId"] ??
    string.Empty,
    entraSection["TenantId"] ??
    string.Empty,
    defaultScope,
    entraSection["AuthorityHost"] ??
    builder.Configuration["ENTRA_AUTHORITY_HOST"] ??
    "https://login.microsoftonline.com",
    authStateFilePath));
builder.Services.AddHttpClient<EntraDeviceFlowClient>();
builder.Services.AddSingleton<IEntraSessionAuthStore, JsonEntraSessionAuthStore>();
builder.Services.AddSingleton<EntraDeviceFlowCoordinator>();
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "web-api-app",
            Version = "1.0.0"
        };
        options.ServerInstructions =
            "Use the exposed tools for single-digit addition, single-digit multiplication, reading the current UTC date and time, and Microsoft Entra ID device-flow sign in tied to the current MCP session.";
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
app.UseHttpLogging();

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
            "get_client_app_id",
            "ms_sign_in",
            "ms_sign_in_status"
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

static class PathResolver
{
    public static string Resolve(string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
    }
}
