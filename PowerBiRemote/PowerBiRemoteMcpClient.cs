using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WebApiApp.PowerBiRemote;

public sealed class PowerBiRemoteMcpClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly PowerBiRemoteMcpOptions _options;
    private readonly ILogger<PowerBiRemoteMcpClient> _logger;

    public PowerBiRemoteMcpClient(
        HttpClient httpClient,
        PowerBiRemoteMcpOptions options,
        ILogger<PowerBiRemoteMcpClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<string?> InitializeAsync(string accessToken, CancellationToken cancellationToken)
    {
        _options.EnsureConfigured();

        var response = await SendRequestAsync(
            accessToken,
            null,
            "initialize",
            new
            {
                protocolVersion = _options.ProtocolVersion,
                capabilities = new { },
                clientInfo = new
                {
                    name = "web-api-app-powerbi-proxy",
                    version = "1.0.0"
                }
            },
            cancellationToken);

        _logger.LogInformation(
            "Initialized remote Power BI MCP session. Remote session id present: {HasSessionId}.",
            !string.IsNullOrWhiteSpace(response.RemoteSessionId));

        return response.RemoteSessionId;
    }

    public async Task<IReadOnlyList<PowerBiRemoteToolDefinition>> ListToolsAsync(
        string accessToken,
        string? remoteSessionId,
        CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            accessToken,
            remoteSessionId,
            "tools/list",
            new { },
            cancellationToken);

        var tools = new List<PowerBiRemoteToolDefinition>();
        if (response.Result.TryGetProperty("tools", out var toolsElement) &&
            toolsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var tool in toolsElement.EnumerateArray())
            {
                var name = tool.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var description = tool.TryGetProperty("description", out var descriptionElement)
                    ? descriptionElement.GetString()
                    : null;
                var inputSchema = tool.TryGetProperty("inputSchema", out var inputSchemaElement)
                    ? inputSchemaElement.Clone()
                    : JsonSerializer.SerializeToElement(new { type = "object", properties = new { } });

                tools.Add(new PowerBiRemoteToolDefinition(name, description, inputSchema));
            }
        }

        _logger.LogInformation("Discovered {ToolCount} remote Power BI MCP tools.", tools.Count);
        return tools;
    }

    public async Task<JsonElement> CallToolAsync(
        string accessToken,
        string? remoteSessionId,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            accessToken,
            remoteSessionId,
            "tools/call",
            new
            {
                name = toolName,
                arguments
            },
            cancellationToken);

        _logger.LogInformation("Remote Power BI MCP tool {ToolName} completed.", toolName);
        return response.Result.Clone();
    }

    private async Task<RemoteMcpResponse> SendRequestAsync(
        string accessToken,
        string? remoteSessionId,
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                jsonrpc = "2.0",
                id = Guid.NewGuid().ToString("N"),
                method,
                @params = parameters
            },
            SerializerOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.EndpointUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", _options.ProtocolVersion);

        if (!string.IsNullOrWhiteSpace(remoteSessionId))
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", remoteSessionId);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var jsonPayload = ExtractJsonPayload(responseBody);
        using var document = JsonDocument.Parse(jsonPayload);

        if (document.RootElement.TryGetProperty("error", out var errorElement))
        {
            var errorCode = errorElement.TryGetProperty("code", out var codeElement)
                ? codeElement.ToString()
                : "unknown";
            var errorMessage = errorElement.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : "Unknown remote MCP error.";

            throw new InvalidOperationException(
                $"Remote Power BI MCP method '{method}' failed with error {errorCode}: {errorMessage}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Remote Power BI MCP method '{method}' failed with HTTP {(int)response.StatusCode}: {jsonPayload}");
        }

        if (!document.RootElement.TryGetProperty("result", out var resultElement))
        {
            throw new InvalidOperationException(
                $"Remote Power BI MCP method '{method}' returned a response without a result payload.");
        }

        var returnedSessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var values)
            ? values.FirstOrDefault()
            : null;

        return new RemoteMcpResponse(resultElement.Clone(), returnedSessionId);
    }

    private static string ExtractJsonPayload(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return trimmed;
        }

        var lines = trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder();

        foreach (var line in lines)
        {
            var normalizedLine = line.TrimEnd('\r');
            if (normalizedLine.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine(normalizedLine[5..].TrimStart());
            }
        }

        var extracted = builder.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(extracted))
        {
            return extracted;
        }

        throw new InvalidOperationException("The remote Power BI MCP server returned an unreadable response body.");
    }

    private sealed record RemoteMcpResponse(JsonElement Result, string? RemoteSessionId);
}
