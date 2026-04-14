using System.Text.Json;
using WebApiApp.EntraAuth;

namespace WebApiApp.PowerBiRemote;

public sealed class PowerBiRemoteProxyCoordinator
{
    private readonly IEntraSessionAuthStore _authStore;
    private readonly PowerBiRemoteMcpClient _remoteMcpClient;
    private readonly ILogger<PowerBiRemoteProxyCoordinator> _logger;

    public PowerBiRemoteProxyCoordinator(
        IEntraSessionAuthStore authStore,
        PowerBiRemoteMcpClient remoteMcpClient,
        ILogger<PowerBiRemoteProxyCoordinator> logger)
    {
        _authStore = authStore;
        _remoteMcpClient = remoteMcpClient;
        _logger = logger;
    }

    public Task<Dictionary<string, object?>> GetSemanticModelSchemaAsync(
        string mcpSessionId,
        string artifactId,
        CancellationToken cancellationToken)
    {
        return CallRemoteToolAsync(
            mcpSessionId,
            "GetSemanticModelSchema",
            artifactId,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["artifactId"] = artifactId
            },
            cancellationToken);
    }

    public Task<Dictionary<string, object?>> GenerateQueryAsync(
        string mcpSessionId,
        string artifactId,
        string userInput,
        Dictionary<string, object?>? schemaSelection,
        List<Dictionary<string, object?>>? chatHistory,
        List<string>? valueSearchTerms,
        CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["artifactId"] = artifactId,
            ["userInput"] = userInput
        };

        if (schemaSelection is not null)
        {
            arguments["schemaSelection"] = schemaSelection;
        }

        if (chatHistory is not null)
        {
            arguments["chatHistory"] = chatHistory;
        }

        if (valueSearchTerms is not null)
        {
            arguments["valueSearchTerms"] = valueSearchTerms;
        }

        return CallRemoteToolAsync(
            mcpSessionId,
            "GenerateQuery",
            artifactId,
            arguments,
            cancellationToken);
    }

    public Task<Dictionary<string, object?>> ExecuteQueryAsync(
        string mcpSessionId,
        string artifactId,
        string daxQuery,
        int? maxRows,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = PowerBiDaxQueryNormalizer.Normalize(daxQuery);
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["artifactId"] = artifactId,
            ["daxQuery"] = normalizedQuery
        };

        if (maxRows is not null)
        {
            arguments["maxRows"] = maxRows.Value;
        }

        return CallRemoteToolAsync(
            mcpSessionId,
            "ExecuteQuery",
            artifactId,
            arguments,
            cancellationToken);
    }

    private async Task<Dictionary<string, object?>> CallRemoteToolAsync(
        string mcpSessionId,
        string remoteToolName,
        string artifactId,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var authState = await _authStore.GetAsync(mcpSessionId, cancellationToken)
            ?? throw new InvalidOperationException(
                "This MCP session is not signed in to Microsoft Entra ID. Call ms_sign_in first.");

        if (authState.Status != EntraLoginStatus.Authenticated || string.IsNullOrWhiteSpace(authState.AccessToken))
        {
            throw new InvalidOperationException(
                "This MCP session does not have a valid Microsoft Entra access token. Sign in again.");
        }

        if (authState.AccessTokenExpiresAtUtc is { } accessTokenExpiresAtUtc &&
            accessTokenExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            throw new InvalidOperationException(
                "The Microsoft Entra access token for this MCP session has expired. Sign in again.");
        }

        _logger.LogInformation(
            "Proxying exact Power BI remote MCP tool {RemoteToolName} for MCP session {McpSessionId} and artifact {ArtifactId}.",
            remoteToolName,
            mcpSessionId,
            artifactId);

        var remoteSessionId = await _remoteMcpClient.InitializeAsync(authState.AccessToken, cancellationToken);
        var remoteTools = await _remoteMcpClient.ListToolsAsync(authState.AccessToken, remoteSessionId, cancellationToken);
        var remoteTool = remoteTools.FirstOrDefault(tool => string.Equals(tool.Name, remoteToolName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"The remote Power BI MCP server does not expose the expected tool '{remoteToolName}'.");

        var result = await _remoteMcpClient.CallToolAsync(
            authState.AccessToken,
            remoteSessionId,
            remoteTool.Name,
            arguments,
            cancellationToken);

        authState.PowerBiSemanticModelId = artifactId;
        await _authStore.SaveAsync(authState, cancellationToken);

        return ConvertJsonObject(result);
    }

    public static Dictionary<string, object?> BuildSchemaSelection(
        PowerBiSchemaSelection? schemaSelection)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["tables"] = schemaSelection?.Tables?.Select(table => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = table.Name,
                ["columns"] = table.Columns,
                ["measures"] = table.Measures
            }).ToList()
        };
    }

    public static Dictionary<string, object?> BuildChatHistoryMessage(
        string role,
        string? content)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["role"] = role,
            ["content"] = content
        };
    }

    private static Dictionary<string, object?> ConvertJsonObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["value"] = ConvertJsonValue(element)
            };
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = ConvertJsonValue(property.Value);
        }

        return result;
    }

    private static object? ConvertJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => ConvertJsonValue(property.Value),
                    StringComparer.Ordinal),
            JsonValueKind.Array => element
                .EnumerateArray()
                .Select(ConvertJsonValue)
                .ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var int64Value) => int64Value,
            JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }
}

public sealed record PowerBiSchemaSelection(
    List<PowerBiSchemaSelectionTable>? Tables);

public sealed record PowerBiSchemaSelectionTable(
    string? Name,
    List<string>? Columns,
    List<string>? Measures);
