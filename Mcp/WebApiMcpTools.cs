using ModelContextProtocol.Server;
using System.ComponentModel;

namespace WebApiApp.Mcp;

public sealed record ClientAppInfo(string ClientAppId);

[McpServerToolType]
public sealed class WebApiMcpTools(ClientAppInfo clientAppInfo)
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
}

public sealed record MathToolResult(string Operation, int Left, int Right, int Result);

public sealed record UtcDateTimeToolResult(string UtcDateTime);

public sealed record ClientAppIdToolResult(string ClientAppId);
