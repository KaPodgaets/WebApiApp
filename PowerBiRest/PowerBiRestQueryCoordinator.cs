using System.Text.Json;
using WebApiApp.EntraAuth;

namespace WebApiApp.PowerBiRest;

public sealed class PowerBiRestQueryCoordinator
{
    private readonly IEntraSessionAuthStore _authStore;
    private readonly PowerBiRestClient _powerBiRestClient;
    private readonly ILogger<PowerBiRestQueryCoordinator> _logger;

    public PowerBiRestQueryCoordinator(
        IEntraSessionAuthStore authStore,
        PowerBiRestClient powerBiRestClient,
        ILogger<PowerBiRestQueryCoordinator> logger)
    {
        _authStore = authStore;
        _powerBiRestClient = powerBiRestClient;
        _logger = logger;
    }

    public async Task<Dictionary<string, object?>> ExecuteDaxAsync(
        string mcpSessionId,
        string daxQuery,
        string? requestedWorkspaceId,
        string? requestedDatasetId,
        int? maxRows,
        bool includeNulls,
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

        var workspaceId = string.IsNullOrWhiteSpace(requestedWorkspaceId)
            ? authState.PowerBiWorkspaceId
            : requestedWorkspaceId.Trim();
        var datasetId = string.IsNullOrWhiteSpace(requestedDatasetId)
            ? authState.PowerBiDatasetId
            : requestedDatasetId.Trim();

        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new InvalidOperationException(
                "A Power BI workspace id is required. Pass workspaceId or reuse a session that has one remembered.");
        }

        if (string.IsNullOrWhiteSpace(datasetId))
        {
            throw new InvalidOperationException(
                "A Power BI dataset id is required. Pass datasetId or reuse a session that has one remembered.");
        }

        var normalizedQuery = NormalizeDaxQuery(daxQuery, maxRows);

        _logger.LogInformation(
            "Executing direct Power BI REST DAX query for MCP session {McpSessionId}, workspace {WorkspaceId}, dataset {DatasetId}.",
            mcpSessionId,
            workspaceId,
            datasetId);

        JsonElement rawResponse;
        try
        {
            rawResponse = await _powerBiRestClient.ExecuteDaxAsync(
                authState.AccessToken,
                workspaceId,
                datasetId,
                normalizedQuery,
                includeNulls,
                cancellationToken);
        }
        catch (PowerBiRestApiException ex) when (ex.StatusCode == (int)System.Net.HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                $"Power BI REST executeQueries returned 403 Forbidden. This usually means the signed-in user lacks Build permission on dataset {datasetId}.",
                ex);
        }
        catch (PowerBiRestApiException ex) when (ex.StatusCode == (int)System.Net.HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                $"Power BI REST executeQueries returned 401 Unauthorized for dataset {datasetId}. Sign in again or verify the app registration and dataset permissions.",
                ex);
        }

        var rows = FlattenRows(rawResponse);
        var columns = rows.Count == 0
            ? new List<string>()
            : rows[0].Keys.ToList();

        authState.PowerBiWorkspaceId = workspaceId;
        authState.PowerBiDatasetId = datasetId;
        await _authStore.SaveAsync(authState, cancellationToken);

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["status"] = "ok",
            ["workspaceId"] = workspaceId,
            ["datasetId"] = datasetId,
            ["rowCount"] = rows.Count,
            ["columns"] = columns,
            ["rows"] = rows,
            ["message"] = rows.Count == 0
                ? "The query executed successfully but returned no rows."
                : null
        };
    }

    private static string NormalizeDaxQuery(string daxQuery, int? maxRows)
    {
        if (string.IsNullOrWhiteSpace(daxQuery))
        {
            throw new InvalidOperationException("The DAX query cannot be empty.");
        }

        var trimmed = daxQuery.Trim();
        if (!trimmed.StartsWith("EVALUATE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The DAX query must begin with EVALUATE for Power BI executeQueries.");
        }

        if (maxRows is null || maxRows <= 0)
        {
            return trimmed;
        }

        var body = trimmed["EVALUATE".Length..].TrimStart();
        return $"EVALUATE TOPN({maxRows.Value}, {body})";
    }

    private static List<Dictionary<string, object?>> FlattenRows(JsonElement response)
    {
        var rows = new List<Dictionary<string, object?>>();
        if (!response.TryGetProperty("results", out var resultsElement) ||
            resultsElement.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }

        foreach (var result in resultsElement.EnumerateArray())
        {
            if (!result.TryGetProperty("tables", out var tablesElement) ||
                tablesElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var table in tablesElement.EnumerateArray())
            {
                if (!table.TryGetProperty("rows", out var rowsElement) ||
                    rowsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var row in rowsElement.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var normalizedRow = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var property in row.EnumerateObject())
                    {
                        normalizedRow[StripDaxBrackets(property.Name)] = ConvertJsonValue(property.Value);
                    }

                    rows.Add(normalizedRow);
                }
            }
        }

        return rows;
    }

    private static string StripDaxBrackets(string name)
    {
        return name.StartsWith('[') && name.EndsWith(']') && name.Length >= 2
            ? name[1..^1]
            : name;
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
