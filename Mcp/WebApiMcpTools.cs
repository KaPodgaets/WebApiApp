using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using Microsoft.AspNetCore.Http;
using System.ComponentModel;
using WebApiApp.EntraAuth;

namespace WebApiApp.Mcp;

public sealed record ClientAppInfo(string ClientAppId);

[McpServerToolType]
public sealed class WebApiMcpTools(
    ClientAppInfo clientAppInfo,
    EntraDeviceFlowCoordinator entraDeviceFlowCoordinator,
    IHttpContextAccessor httpContextAccessor,
    ILogger<WebApiMcpTools> logger)
{
    [McpServerTool(
        Name = "sum_digits",
        Title = "Sum Digits",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        ReadOnly = true,
        UseStructuredContent = true)]
    [Description("Adds two single-digit integers and returns the result.")]
    public MathToolResult SumDigits(
        [Description("First digit from 0 through 9.")] int left,
        [Description("Second digit from 0 through 9.")] int right)
    {
        ValidateDigit(left, nameof(left));
        ValidateDigit(right, nameof(right));

        return new("sum", left, right, left + right);
    }

    [McpServerTool(
        Name = "multiply_digits",
        Title = "Multiply Digits",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        ReadOnly = true,
        UseStructuredContent = true)]
    [Description("Multiplies two single-digit integers and returns the result.")]
    public MathToolResult MultiplyDigits(
        [Description("First digit from 0 through 9.")] int left,
        [Description("Second digit from 0 through 9.")] int right)
    {
        ValidateDigit(left, nameof(left));
        ValidateDigit(right, nameof(right));

        return new("multiplication", left, right, left * right);
    }

    [McpServerTool(
        Name = "get_utc_datetime",
        Title = "Get UTC Datetime",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        ReadOnly = true,
        UseStructuredContent = true)]
    [Description("Returns the current UTC date and time in ISO 8601 format.")]
    public UtcDateTimeToolResult GetUtcDatetime()
    {
        return new(DateTimeOffset.UtcNow.ToString("O"));
    }

    [McpServerTool(
        Name = "get_client_app_id",
        Title = "Get Client App Id",
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        ReadOnly = true,
        UseStructuredContent = true)]
    [Description("Returns the configured client application id from the environment.")]
    public ClientAppIdToolResult GetClientAppId()
    {
        return new(clientAppInfo.ClientAppId);
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
            return entraDeviceFlowCoordinator.GetStatusAsync(mcpSessionId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MCP tool ms_sign_in_status failed before status could be returned.");
            return Task.FromResult(new MsSignInStatusToolResult(
                "failed",
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                false,
                ex.Message));
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
}

public sealed record MathToolResult(string Operation, int Left, int Right, int Result);

public sealed record UtcDateTimeToolResult(string UtcDateTime);

public sealed record ClientAppIdToolResult(string ClientAppId);
