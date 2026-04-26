using System.Text.Json;
using WebApiApp.EntraAuth;
using WebApiApp.PowerBiRemote;

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

        var normalizedQuery = NormalizeDaxQuery(
            PowerBiDaxQueryNormalizer.Normalize(daxQuery),
            maxRows);

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
        catch (PowerBiRestApiException ex) when (ex.StatusCode == (int)System.Net.HttpStatusCode.BadRequest)
        {
            throw new InvalidOperationException(
                $"Power BI REST executeQueries returned 400 Bad Request. This usually means the DAX query is invalid for the target dataset or the payload shape is not accepted. Detail: {ex.Message}",
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

    public async Task<Dictionary<string, object?>> ListWorkspacesAndModelsAsync(
        string mcpSessionId,
        CancellationToken cancellationToken)
    {
        var authState = await RequireAuthenticatedSessionAsync(mcpSessionId, cancellationToken);

        _logger.LogInformation(
            "Listing Power BI workspaces and semantic models for MCP session {McpSessionId}.",
            mcpSessionId);

        JsonElement workspacesResponse;
        try
        {
            workspacesResponse = await _powerBiRestClient.ListWorkspacesAsync(
                authState.AccessToken!,
                cancellationToken);
        }
        catch (PowerBiRestApiException ex) when (ex.StatusCode == (int)System.Net.HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                "Power BI REST groups/list returned 403 Forbidden. Verify the signed-in user can access Power BI workspaces.",
                ex);
        }
        catch (PowerBiRestApiException ex) when (ex.StatusCode == (int)System.Net.HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "Power BI REST groups/list returned 401 Unauthorized. Sign in again or verify the app registration permissions.",
                ex);
        }

        var workspaces = new List<Dictionary<string, object?>>();
        var datasetCount = 0;

        if (workspacesResponse.TryGetProperty("value", out var workspacesElement) &&
            workspacesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var workspaceElement in workspacesElement.EnumerateArray())
            {
                var workspaceId = GetStringProperty(workspaceElement, "id");
                if (string.IsNullOrWhiteSpace(workspaceId))
                {
                    continue;
                }

                JsonElement datasetsResponse;
                try
                {
                    datasetsResponse = await _powerBiRestClient.ListDatasetsAsync(
                        authState.AccessToken!,
                        workspaceId,
                        cancellationToken);
                }
                catch (PowerBiRestApiException ex) when (ex.StatusCode == (int)System.Net.HttpStatusCode.Forbidden)
                {
                    throw new InvalidOperationException(
                        $"Power BI REST datasets/list returned 403 Forbidden for workspace {workspaceId}. Verify the signed-in user can access datasets in that workspace.",
                        ex);
                }
                catch (PowerBiRestApiException ex) when (ex.StatusCode == (int)System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new InvalidOperationException(
                        $"Power BI REST datasets/list returned 401 Unauthorized for workspace {workspaceId}. Sign in again or verify the app registration permissions.",
                        ex);
                }

                var models = new List<Dictionary<string, object?>>();
                if (datasetsResponse.TryGetProperty("value", out var datasetsElement) &&
                    datasetsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var datasetElement in datasetsElement.EnumerateArray())
                    {
                        models.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["id"] = GetStringProperty(datasetElement, "id"),
                            ["name"] = GetStringProperty(datasetElement, "name"),
                            ["configuredBy"] = GetStringProperty(datasetElement, "configuredBy"),
                            ["isRefreshable"] = GetBooleanProperty(datasetElement, "isRefreshable"),
                            ["isEffectiveIdentityRequired"] = GetBooleanProperty(datasetElement, "isEffectiveIdentityRequired"),
                            ["isEffectiveIdentityRolesRequired"] = GetBooleanProperty(datasetElement, "isEffectiveIdentityRolesRequired"),
                            ["targetStorageMode"] = GetStringProperty(datasetElement, "targetStorageMode")
                        });
                    }
                }

                datasetCount += models.Count;
                workspaces.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["id"] = workspaceId,
                    ["name"] = GetStringProperty(workspaceElement, "name"),
                    ["isReadOnly"] = GetBooleanProperty(workspaceElement, "isReadOnly"),
                    ["isOnDedicatedCapacity"] = GetBooleanProperty(workspaceElement, "isOnDedicatedCapacity"),
                    ["capacityId"] = GetStringProperty(workspaceElement, "capacityId"),
                    ["models"] = models
                });
            }
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["status"] = "ok",
            ["workspaceCount"] = workspaces.Count,
            ["modelCount"] = datasetCount,
            ["workspaces"] = workspaces
        };
    }

    private async Task<EntraSessionAuthState> RequireAuthenticatedSessionAsync(
        string mcpSessionId,
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

        return authState;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool? GetBooleanProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string NormalizeDaxQuery(string daxQuery, int? maxRows)
    {
        if (string.IsNullOrWhiteSpace(daxQuery))
        {
            throw new InvalidOperationException("The DAX query cannot be empty.");
        }

        var trimmed = daxQuery.Trim();
        if (!trimmed.Contains("EVALUATE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The DAX query must contain EVALUATE for Power BI executeQueries.");
        }

        if (maxRows is null || maxRows <= 0)
        {
            return trimmed;
        }

        // Only rewrite simple EVALUATE queries. Multi-statement DAX or queries with ORDER BY
        // can be made invalid if we blindly wrap them in TOPN.
        if (!trimmed.StartsWith("EVALUATE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("DEFINE", StringComparison.OrdinalIgnoreCase))
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
