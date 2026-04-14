using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WebApiApp.PowerBiRest;

public sealed class PowerBiRestClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly PowerBiRestOptions _options;
    private readonly ILogger<PowerBiRestClient> _logger;

    public PowerBiRestClient(
        HttpClient httpClient,
        PowerBiRestOptions options,
        ILogger<PowerBiRestClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<JsonElement> ExecuteDaxAsync(
        string accessToken,
        string workspaceId,
        string datasetId,
        string daxQuery,
        bool includeNulls,
        CancellationToken cancellationToken)
    {
        _options.EnsureConfigured();

        var endpoint = $"{_options.BaseUrl.TrimEnd('/')}/groups/{workspaceId}/datasets/{datasetId}/executeQueries";
        var payload = JsonSerializer.Serialize(
            new
            {
                queries = new[]
                {
                    new
                    {
                        query = daxQuery
                    }
                },
                serializerSettings = new
                {
                    includeNulls
                }
            },
            SerializerOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        JsonElement responseElement;
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            responseElement = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Power BI REST executeQueries returned unreadable JSON with HTTP {(int)response.StatusCode}.",
                ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var detail = ExtractErrorMessage(responseElement) ?? response.ReasonPhrase ?? "Unknown error";
            if (string.Equals(detail, "Bad Request", StringComparison.OrdinalIgnoreCase))
            {
                detail = responseBody;
            }

            _logger.LogWarning(
                "Power BI REST executeQueries failed with HTTP {StatusCode} for workspace {WorkspaceId}, dataset {DatasetId}. Detail: {Detail}",
                (int)response.StatusCode,
                workspaceId,
                datasetId,
                detail);
            throw new PowerBiRestApiException((int)response.StatusCode, detail);
        }

        _logger.LogInformation(
            "Power BI REST executeQueries succeeded for workspace {WorkspaceId}, dataset {DatasetId}.",
            workspaceId,
            datasetId);

        return responseElement;
    }

    private static string? ExtractErrorMessage(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("error", out var errorElement) ||
            errorElement.ValueKind != JsonValueKind.Object)
        {
            return element.ToString();
        }

        var message = errorElement.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString()
            : null;

        if (errorElement.TryGetProperty("details", out var detailsElement) &&
            detailsElement.ValueKind == JsonValueKind.Array)
        {
            var detailMessages = detailsElement
                .EnumerateArray()
                .Select(detail => detail.TryGetProperty("message", out var detailMessage)
                    ? detailMessage.GetString()
                    : detail.ToString())
                .Where(detail => !string.IsNullOrWhiteSpace(detail))
                .ToArray();

            if (detailMessages.Length > 0)
            {
                return string.IsNullOrWhiteSpace(message)
                    ? string.Join(" | ", detailMessages)
                    : $"{message} | {string.Join(" | ", detailMessages)}";
            }
        }

        return message;
    }
}

public sealed class PowerBiRestApiException : Exception
{
    public PowerBiRestApiException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
