using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using Microsoft.AspNetCore.Http;
using System.ComponentModel;
using System.Text.Json;
using WebApiApp.EntraAuth;
using WebApiApp.PowerBiRest;
using WebApiApp.PowerBiRemote;

namespace WebApiApp.Mcp;

public sealed record ClientAppInfo(string ClientAppId);

[McpServerToolType]
public sealed class WebApiMcpTools(
    ClientAppInfo clientAppInfo,
    WorkflowInstructionCatalog workflowInstructionCatalog,
    EntraDeviceFlowCoordinator entraDeviceFlowCoordinator,
    PowerBiRemoteProxyCoordinator powerBiRemoteProxyCoordinator,
    PowerBiRestQueryCoordinator powerBiRestQueryCoordinator,
    IHttpContextAccessor httpContextAccessor,
    ILogger<WebApiMcpTools> logger)
{
    private static readonly JsonSerializerOptions LogSerializerOptions = new(JsonSerializerDefaults.Web);

    [Description("Adds two single-digit integers and returns the result.")]
    public MathToolResult SumDigits(
        [Description("First digit from 0 through 9.")] int left,
        [Description("Second digit from 0 through 9.")] int right)
    {
        ValidateDigit(left, nameof(left));
        ValidateDigit(right, nameof(right));

        return new("sum", left, right, left + right);
    }

    [Description("Multiplies two single-digit integers and returns the result.")]
    public MathToolResult MultiplyDigits(
        [Description("First digit from 0 through 9.")] int left,
        [Description("Second digit from 0 through 9.")] int right)
    {
        ValidateDigit(left, nameof(left));
        ValidateDigit(right, nameof(right));

        return new("multiplication", left, right, left * right);
    }

    [Description("Returns the current UTC date and time in ISO 8601 format.")]
    public UtcDateTimeToolResult GetUtcDatetime()
    {
        return new(DateTimeOffset.UtcNow.ToString("O"));
    }

    [Description("Returns the configured client application id from the environment.")]
    public ClientAppIdToolResult GetClientAppId()
    {
        return new(clientAppInfo.ClientAppId);
    }

    [McpServerTool(
        Name = "discover_workflow",
        Title = "Discover Workflow",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        ReadOnly = true,
        UseStructuredContent = true)]
    [Description("Returns the default analytical workflow instruction for the MCP client, including sign-in, theme discovery, knowledge lookup, and DAX execution guidance.")]
    public WorkflowInstructionToolResult DiscoverWorkflow()
    {
        var instruction = workflowInstructionCatalog.GetDiscoverWorkflowInstruction();
        var result = new WorkflowInstructionToolResult(
            instruction.Name,
            instruction.Title,
            instruction.FileName,
            instruction.Markdown);

        logger.LogInformation(
            "MCP tool discover_workflow result loaded from {FileName}.",
            instruction.FileName);

        return result;
    }

    [McpServerTool(
        Name = "powerbi_list_workspaces_and_models_rest",
        Title = "Power BI List Workspaces And Models REST",
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        ReadOnly = true,
        UseStructuredContent = true)]
    [Description("Lists accessible Power BI workspaces and their semantic models by calling the Power BI REST API with the current MCP session's signed-in Entra token.")]
    public async Task<Dictionary<string, object?>> PowerBiListWorkspacesAndModelsRest(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        var mcpSessionId = ResolveMcpSessionId(request, httpContextAccessor);
        logger.LogInformation(
            "MCP tool powerbi_list_workspaces_and_models_rest called for session {McpSessionId}.",
            mcpSessionId);

        try
        {
            return await powerBiRestQueryCoordinator.ListWorkspacesAndModelsAsync(
                mcpSessionId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "MCP tool powerbi_list_workspaces_and_models_rest failed for session {McpSessionId}.",
                mcpSessionId);

            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["status"] = "failed",
                ["workspaceCount"] = 0,
                ["modelCount"] = 0,
                ["workspaces"] = Array.Empty<object>(),
                ["errorMessage"] = ex.Message
            };
        }
    }

    [McpServerTool(
        Name = "ms_sign_in",
        Title = "MS Sign In",
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        ReadOnly = false,
        UseStructuredContent = true)]
    [Description("Starts Microsoft Entra ID device-flow sign in for the current MCP session. Each call resets any previous sign-in state for this session.")]
    public Task<MsSignInStartToolResult> MsSignIn(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken,
        [Description("Optional delegated OAuth scope string. If omitted, the server default scope is used.")] string? scope = null)
    {
        var mcpSessionId = ResolveMcpSessionId(request, httpContextAccessor);
        logger.LogInformation(
            "MCP tool ms_sign_in called for session {McpSessionId}. Scope provided: {HasScope}.",
            mcpSessionId,
            !string.IsNullOrWhiteSpace(scope));
        return entraDeviceFlowCoordinator.StartSignInAsync(mcpSessionId, scope, cancellationToken);
    }

    [McpServerTool(
        Name = "ms_sign_in_status",
        Title = "MS Sign In Status",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        ReadOnly = true,
        UseStructuredContent = true)]
    [Description("Returns the current Microsoft Entra ID sign-in status for the current MCP session.")]
    public Task<MsSignInStatusToolResult> MsSignInStatus(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        try
        {
            var mcpSessionId = ResolveMcpSessionId(request, httpContextAccessor);
            logger.LogInformation("MCP tool ms_sign_in_status called for session {McpSessionId}.", mcpSessionId);
            return LogStatusResultAsync(
                mcpSessionId,
                entraDeviceFlowCoordinator.GetStatusAsync(mcpSessionId, cancellationToken));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MCP tool ms_sign_in_status failed before status could be returned.");
            return Task.FromResult(new MsSignInStatusToolResult(
                "failed",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                false,
                ex.Message));
        }
    }

    [Description("Returns a minimal Microsoft Entra ID sign-in status shape for the current MCP session. Use this to debug MCP client compatibility with tool responses.")]
    public async Task<MsSignInStatusMinimalToolResult> MsSignInStatusMinimal(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        try
        {
            var mcpSessionId = ResolveMcpSessionId(request, httpContextAccessor);
            logger.LogInformation("MCP tool ms_sign_in_status_minimal called for session {McpSessionId}.", mcpSessionId);
            var status = await entraDeviceFlowCoordinator.GetStatusAsync(mcpSessionId, cancellationToken);
            var result = new MsSignInStatusMinimalToolResult(
                status.Status,
                status.HasAccessToken,
                status.LastError ?? string.Empty);

            logger.LogInformation(
                "MCP tool ms_sign_in_status_minimal result for session {McpSessionId}: {ResultJson}",
                mcpSessionId,
                JsonSerializer.Serialize(result, LogSerializerOptions));

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MCP tool ms_sign_in_status_minimal failed before status could be returned.");
            return new MsSignInStatusMinimalToolResult("failed", false, ex.Message);
        }
    }

    [Description("Returns a tiny hardcoded result to help debug MCP client handling of simple structured tool responses.")]
    public EchoOkToolResult McpEchoOk()
    {
        var result = new EchoOkToolResult("ok", "probe");
        logger.LogInformation("MCP tool mcp_echo_ok result: {ResultJson}", JsonSerializer.Serialize(result, LogSerializerOptions));
        return result;
    }

    [McpServerTool(
        Name = "mcp_echo_status",
        Title = "MCP Echo Status",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        ReadOnly = true,
        UseStructuredContent = true)]
    [Description("Returns a tiny hardcoded status-shaped result to help debug MCP client handling of simple status objects.")]
    public EchoStatusToolResult McpEchoStatus()
    {
        var result = new EchoStatusToolResult("authenticated", true, string.Empty);
        logger.LogInformation("MCP tool mcp_echo_status result: {ResultJson}", JsonSerializer.Serialize(result, LogSerializerOptions));
        return result;
    }

    [McpServerTool(
        Name = "powerbi_get_semantic_model_schema",
        Title = "Power BI Get Semantic Model Schema",
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        ReadOnly = true,
        UseStructuredContent = true)]
    [Description("Proxy for the remote Power BI MCP tool GetSemanticModelSchema. Retrieves semantic model schema, custom instructions, and verified answers for the specified artifact id.")]
    public async Task<Dictionary<string, object?>> PowerBiGetSemanticModelSchema(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken,
        [Description("The GUID of the artifact (semantic model or report) to fetch the schema for.")] string artifactId)
    {
        var mcpSessionId = ResolveMcpSessionId(request, httpContextAccessor);
        logger.LogInformation(
            "MCP tool powerbi_get_semantic_model_schema called for session {McpSessionId} and artifact {ArtifactId}.",
            mcpSessionId,
            artifactId);

        return await powerBiRemoteProxyCoordinator.GetSemanticModelSchemaAsync(
            mcpSessionId,
            artifactId,
            cancellationToken);
    }

    [Description("Proxy for the remote Power BI MCP tool GenerateQuery. Generates a DAX query for the given artifact id and user input.")]
    public async Task<Dictionary<string, object?>> PowerBiGenerateQuery(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken,
        [Description("The GUID of the artifact (semantic model or report) to generate the DAX query on.")] string artifactId,
        [Description("The user's input for which the DAX query should be generated.")] string userInput,
        [Description("Optional schema context with the relevant tables, columns, and measures.")] PowerBiSchemaSelection? schemaSelection = null,
        [Description("Optional chat history for contextual DAX generation.")] List<PowerBiChatHistoryMessage>? chatHistory = null,
        [Description("Optional list of specific data values mentioned in the user's question.")] List<string>? valueSearchTerms = null)
    {
        var mcpSessionId = ResolveMcpSessionId(request, httpContextAccessor);
        logger.LogInformation(
            "MCP tool powerbi_generate_query called for session {McpSessionId} and artifact {ArtifactId}.",
            mcpSessionId,
            artifactId);

        return await powerBiRemoteProxyCoordinator.GenerateQueryAsync(
            mcpSessionId,
            artifactId,
            userInput,
            schemaSelection is null ? null : PowerBiRemoteProxyCoordinator.BuildSchemaSelection(schemaSelection),
            chatHistory?.Select(message => PowerBiRemoteProxyCoordinator.BuildChatHistoryMessage(message.Role, message.Content)).ToList(),
            valueSearchTerms,
            cancellationToken);
    }

    [Description("Proxy for the remote Power BI MCP tool ExecuteQuery. Executes a DAX query for the given artifact id and returns the remote result.")]
    public async Task<Dictionary<string, object?>> PowerBiExecuteQuery(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken,
        [Description("The GUID of the artifact (semantic model or report) to execute the DAX query against.")] string artifactId,
        [Description("The DAX query to execute against the underlying Power BI semantic model.")] string daxQuery,
        [Description("Optional maximum number of rows to return. Default remote behavior is 250, maximum is 1000.")] int? maxRows = null)
    {
        var mcpSessionId = ResolveMcpSessionId(request, httpContextAccessor);
        logger.LogInformation(
            "MCP tool powerbi_execute_query called for session {McpSessionId} and artifact {ArtifactId}.",
            mcpSessionId,
            artifactId);

        try
        {
            return await powerBiRemoteProxyCoordinator.ExecuteQueryAsync(
                mcpSessionId,
                artifactId,
                daxQuery,
                maxRows,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "MCP tool powerbi_execute_query failed for session {McpSessionId} and artifact {ArtifactId}.",
                mcpSessionId,
                artifactId);

            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["status"] = "failed",
                ["artifactId"] = artifactId,
                ["errorMessage"] = ex.Message
            };
        }
    }

    [McpServerTool(
        Name = "powerbi_execute_dax_rest",
        Title = "Power BI Execute DAX REST",
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        ReadOnly = false,
        UseStructuredContent = true)]
    [Description("Executes a DAX query directly against the Power BI REST executeQueries endpoint using the signed-in user's access token and returns the flattened result rows.")]
    public async Task<Dictionary<string, object?>> PowerBiExecuteDaxRest(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken,
        [Description("A valid DAX query to execute. It should begin with EVALUATE.")] string daxQuery,
        [Description("Optional GUID of the Power BI workspace that owns the dataset. If omitted, the server reuses the current MCP session's remembered workspace id.")] string? workspaceId = null,
        [Description("Optional GUID of the Power BI dataset (semantic model) to query. If omitted, the server reuses the current MCP session's remembered dataset id.")] string? datasetId = null,
        [Description("Optional maximum number of rows to return by wrapping the DAX query in TOPN at the engine level.")] int? maxRows = null,
        [Description("Whether null values should be included in the Power BI REST response. Defaults to true.")] bool includeNulls = true)
    {
        var mcpSessionId = ResolveMcpSessionId(request, httpContextAccessor);
        logger.LogInformation(
            "MCP tool powerbi_execute_dax_rest called for session {McpSessionId}, workspace {WorkspaceId}, dataset {DatasetId}.",
            mcpSessionId,
            workspaceId ?? "<remembered>",
            datasetId ?? "<remembered>");

        try
        {
            return await powerBiRestQueryCoordinator.ExecuteDaxAsync(
                mcpSessionId,
                daxQuery,
                workspaceId,
                datasetId,
                maxRows,
                includeNulls,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "MCP tool powerbi_execute_dax_rest failed for session {McpSessionId}, workspace {WorkspaceId}, dataset {DatasetId}.",
                mcpSessionId,
                workspaceId ?? "<remembered>",
                datasetId ?? "<remembered>");

            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["status"] = "failed",
                ["workspaceId"] = workspaceId,
                ["datasetId"] = datasetId,
                ["rowCount"] = 0,
                ["columns"] = Array.Empty<string>(),
                ["rows"] = Array.Empty<object>(),
                ["errorMessage"] = ex.Message
            };
        }
    }

    private static void ValidateDigit(int value, string parameterName)
    {
        if (value is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The value must be a single digit between 0 and 9.");
        }
    }

    private static string ResolveMcpSessionId(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor httpContextAccessor)
    {
        var sessionId = request.JsonRpcRequest.Context?.RelatedTransport?.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            sessionId = httpContextAccessor.HttpContext?.Request.Headers["Mcp-Session-Id"].ToString();
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException(
                "The current MCP request does not have an associated session id. Initialize the MCP session before calling this tool.");
        }

        return sessionId;
    }

    private async Task<MsSignInStatusToolResult> LogStatusResultAsync(
        string mcpSessionId,
        Task<MsSignInStatusToolResult> statusTask)
    {
        var result = await statusTask;
        logger.LogInformation(
            "MCP tool ms_sign_in_status result for session {McpSessionId}: {ResultJson}",
            mcpSessionId,
            JsonSerializer.Serialize(result, LogSerializerOptions));
        return result;
    }
}

public sealed record MathToolResult(string Operation, int Left, int Right, int Result);

public sealed record UtcDateTimeToolResult(string UtcDateTime);

public sealed record ClientAppIdToolResult(string ClientAppId);

public sealed record WorkflowInstructionToolResult(
    string Name,
    string Title,
    string FileName,
    string Markdown);

public sealed record PowerBiChatHistoryMessage(
    string Role,
    string? Content);

public sealed record MsSignInStatusMinimalToolResult(
    string Status,
    bool HasAccessToken,
    string LastError);

public sealed record EchoOkToolResult(
    string Status,
    string Message);

public sealed record EchoStatusToolResult(
    string Status,
    bool HasAccessToken,
    string LastError);
