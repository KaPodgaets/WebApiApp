namespace WebApiApp.EntraAuth;

public enum EntraLoginStatus
{
    SignedOut,
    PendingUserAction,
    Authenticated,
    Failed
}

public sealed class EntraSessionAuthState
{
    public string McpSessionId { get; init; } = string.Empty;
    public string LoginAttemptId { get; init; } = Guid.NewGuid().ToString("N");
    public EntraLoginStatus Status { get; set; } = EntraLoginStatus.SignedOut;
    public string? VerificationUri { get; set; }
    public string? UserCode { get; set; }
    public string? DeviceCode { get; set; }
    public string? Scope { get; set; }
    public string? Message { get; set; }
    public int PollIntervalSeconds { get; set; }
    public DateTimeOffset? DeviceCodeExpiresAtUtc { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record MsSignInStartToolResult(
    string Status,
    string LoginAttemptId,
    string VerificationUri,
    string UserCode,
    string? Message,
    string Scope,
    DateTimeOffset ExpiresAtUtc);

public sealed record MsSignInStatusToolResult(
    string Status,
    string? LoginAttemptId,
    string? VerificationUri,
    string? UserCode,
    string? Scope,
    DateTimeOffset? DeviceCodeExpiresAtUtc,
    DateTimeOffset? AccessTokenExpiresAtUtc,
    bool HasAccessToken,
    bool HasRefreshToken,
    string? LastError);
