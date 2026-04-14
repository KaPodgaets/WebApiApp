using System.Collections.Concurrent;

namespace WebApiApp.EntraAuth;

public sealed class EntraDeviceFlowCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pollers = new(StringComparer.Ordinal);
    private readonly EntraAuthOptions _options;
    private readonly EntraDeviceFlowClient _entraDeviceFlowClient;
    private readonly IEntraSessionAuthStore _authStore;
    private readonly ILogger<EntraDeviceFlowCoordinator> _logger;

    public EntraDeviceFlowCoordinator(
        EntraAuthOptions options,
        EntraDeviceFlowClient entraDeviceFlowClient,
        IEntraSessionAuthStore authStore,
        ILogger<EntraDeviceFlowCoordinator> logger)
    {
        _options = options;
        _entraDeviceFlowClient = entraDeviceFlowClient;
        _authStore = authStore;
        _logger = logger;
    }

    public async Task<MsSignInStartToolResult> StartSignInAsync(
        string mcpSessionId,
        string? requestedScope,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Microsoft Entra device-flow sign-in for MCP session {McpSessionId}.", mcpSessionId);
        _options.EnsureConfigured();

        var sessionLock = _sessionLocks.GetOrAdd(mcpSessionId, static _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync(cancellationToken);

        try
        {
            var scope = string.IsNullOrWhiteSpace(requestedScope)
                ? _options.DefaultScope
                : requestedScope.Trim();

            await ResetSessionInternalAsync(mcpSessionId, cancellationToken);
            _logger.LogDebug("Previous auth state reset for MCP session {McpSessionId}.", mcpSessionId);

            var start = await _entraDeviceFlowClient.StartDeviceFlowAsync(scope, cancellationToken);
            var loginAttemptId = Guid.NewGuid().ToString("N");
            var state = new EntraSessionAuthState
            {
                McpSessionId = mcpSessionId,
                LoginAttemptId = loginAttemptId,
                Status = EntraLoginStatus.PendingUserAction,
                VerificationUri = start.VerificationUri,
                UserCode = start.UserCode,
                DeviceCode = start.DeviceCode,
                Scope = scope,
                Message = start.Message,
                PollIntervalSeconds = start.PollIntervalSeconds,
                DeviceCodeExpiresAtUtc = start.ExpiresAtUtc,
                LastError = null,
                AccessToken = null,
                RefreshToken = null,
                AccessTokenExpiresAtUtc = null
            };

            await _authStore.SaveAsync(state, cancellationToken);
            _logger.LogInformation(
                "Microsoft Entra device code created for MCP session {McpSessionId}, login attempt {LoginAttemptId}, expires at {ExpiresAtUtc}.",
                mcpSessionId,
                loginAttemptId,
                start.ExpiresAtUtc);

            var pollerCts = new CancellationTokenSource();
            if (_pollers.TryGetValue(mcpSessionId, out var previousPoller))
            {
                previousPoller.Cancel();
                previousPoller.Dispose();
            }

            _pollers[mcpSessionId] = pollerCts;

            _ = Task.Run(
                () => PollForTokenAsync(mcpSessionId, loginAttemptId, pollerCts, pollerCts.Token),
                CancellationToken.None);
            _logger.LogDebug(
                "Background Microsoft Entra polling started for MCP session {McpSessionId}, login attempt {LoginAttemptId}.",
                mcpSessionId,
                loginAttemptId);

            return new MsSignInStartToolResult(
                ToToolStatus(state.Status),
                loginAttemptId,
                start.VerificationUri,
                start.UserCode,
                start.Message,
                scope,
                start.ExpiresAtUtc);
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public async Task<MsSignInStatusToolResult> GetStatusAsync(
        string mcpSessionId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Loading Microsoft Entra sign-in status for MCP session {McpSessionId}.", mcpSessionId);
        var state = await _authStore.GetAsync(mcpSessionId, cancellationToken);
        if (state is null)
        {
            return new MsSignInStatusToolResult(
                ToToolStatus(EntraLoginStatus.SignedOut),
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                false,
                null);
        }

        _logger.LogInformation(
            "Microsoft Entra sign-in status for MCP session {McpSessionId}: {Status}.",
            mcpSessionId,
            state.Status);

        return new MsSignInStatusToolResult(
            ToToolStatus(state.Status),
            state.LoginAttemptId,
            state.PowerBiSemanticModelId,
            state.VerificationUri,
            state.UserCode,
            state.Scope,
            state.DeviceCodeExpiresAtUtc,
            state.AccessTokenExpiresAtUtc,
            !string.IsNullOrWhiteSpace(state.AccessToken),
            !string.IsNullOrWhiteSpace(state.RefreshToken),
            state.LastError);
    }

    private async Task ResetSessionInternalAsync(string mcpSessionId, CancellationToken cancellationToken)
    {
        if (_pollers.TryRemove(mcpSessionId, out var poller))
        {
            poller.Cancel();
            poller.Dispose();
        }

        await _authStore.RemoveAsync(mcpSessionId, cancellationToken);
    }

    private async Task PollForTokenAsync(
        string mcpSessionId,
        string loginAttemptId,
        CancellationTokenSource pollerCts,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var current = await _authStore.GetAsync(mcpSessionId, cancellationToken);
                if (current is null || current.LoginAttemptId != loginAttemptId)
                {
                    return;
                }

                if (current.DeviceCodeExpiresAtUtc is { } expiresAtUtc &&
                    expiresAtUtc <= DateTimeOffset.UtcNow)
                {
                    await FailAttemptAsync(
                        mcpSessionId,
                        loginAttemptId,
                        "The Microsoft Entra device code expired before sign-in completed.",
                        cancellationToken);
                    return;
                }

                var delaySeconds = current.PollIntervalSeconds > 0 ? current.PollIntervalSeconds : 5;
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);

                current = await _authStore.GetAsync(mcpSessionId, cancellationToken);
                if (current is null || current.LoginAttemptId != loginAttemptId)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(current.DeviceCode))
                {
                    await FailAttemptAsync(
                        mcpSessionId,
                        loginAttemptId,
                        "The Microsoft Entra device code was not available for token polling.",
                        cancellationToken);
                    return;
                }

                EntraTokenPollResult result;
                try
                {
                    result = await _entraDeviceFlowClient.PollForTokenAsync(current.DeviceCode, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Microsoft Entra token polling failed for MCP session {McpSessionId}.", mcpSessionId);
                    await FailAttemptAsync(
                        mcpSessionId,
                        loginAttemptId,
                        $"Token polling failed: {ex.Message}",
                        cancellationToken);
                    return;
                }

                if (result.IsSuccess)
                {
                    current = await _authStore.GetAsync(mcpSessionId, cancellationToken);
                    if (current is null || current.LoginAttemptId != loginAttemptId)
                    {
                        return;
                    }

                    current.Status = EntraLoginStatus.Authenticated;
                    current.DeviceCode = null;
                    current.AccessToken = result.AccessToken;
                    current.RefreshToken = result.RefreshToken;
                    current.AccessTokenExpiresAtUtc = result.AccessTokenExpiresAtUtc;
                    current.Scope = string.IsNullOrWhiteSpace(result.Scope) ? current.Scope : result.Scope;
                    current.LastError = null;
                    current.Message = "Microsoft Entra sign-in completed.";

                    await _authStore.SaveAsync(current, cancellationToken);
                    _logger.LogInformation(
                        "Microsoft Entra sign-in completed for MCP session {McpSessionId}, login attempt {LoginAttemptId}.",
                        mcpSessionId,
                        loginAttemptId);
                    return;
                }

                switch (result.Error)
                {
                    case "authorization_pending":
                        _logger.LogDebug(
                            "Microsoft Entra authorization is still pending for MCP session {McpSessionId}, login attempt {LoginAttemptId}.",
                            mcpSessionId,
                            loginAttemptId);
                        continue;
                    case "slow_down":
                        current.PollIntervalSeconds = Math.Max(current.PollIntervalSeconds + 5, 5);
                        current.LastError = result.ErrorDescription ?? "Microsoft Entra requested slower polling.";
                        await _authStore.SaveAsync(current, cancellationToken);
                        _logger.LogWarning(
                            "Microsoft Entra requested slower polling for MCP session {McpSessionId}, login attempt {LoginAttemptId}. New interval: {PollIntervalSeconds}s.",
                            mcpSessionId,
                            loginAttemptId,
                            current.PollIntervalSeconds);
                        continue;
                    case "authorization_declined":
                        await FailAttemptAsync(
                            mcpSessionId,
                            loginAttemptId,
                            result.ErrorDescription ?? "The user declined the Microsoft Entra sign-in request.",
                            cancellationToken);
                        return;
                    case "expired_token":
                        await FailAttemptAsync(
                            mcpSessionId,
                            loginAttemptId,
                            result.ErrorDescription ?? "The Microsoft Entra device code expired.",
                            cancellationToken);
                        return;
                    default:
                        await FailAttemptAsync(
                            mcpSessionId,
                            loginAttemptId,
                            result.ErrorDescription ?? $"Microsoft Entra sign-in failed with error '{result.Error ?? "unknown_error"}'.",
                            cancellationToken);
                        return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Microsoft Entra polling cancelled for MCP session {McpSessionId}.", mcpSessionId);
        }
        finally
        {
            if (_pollers.TryGetValue(mcpSessionId, out var activePoller) && ReferenceEquals(activePoller, pollerCts))
            {
                _pollers.TryRemove(mcpSessionId, out _);
                pollerCts.Dispose();
            }
        }
    }

    private async Task FailAttemptAsync(
        string mcpSessionId,
        string loginAttemptId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var current = await _authStore.GetAsync(mcpSessionId, cancellationToken);
        if (current is null || current.LoginAttemptId != loginAttemptId)
        {
            return;
        }

        current.Status = EntraLoginStatus.Failed;
        current.DeviceCode = null;
        current.AccessToken = null;
        current.RefreshToken = null;
        current.AccessTokenExpiresAtUtc = null;
        current.LastError = errorMessage;
        current.Message = null;

        await _authStore.SaveAsync(current, cancellationToken);
        _logger.LogWarning(
            "Microsoft Entra sign-in failed for MCP session {McpSessionId}, login attempt {LoginAttemptId}. Error: {ErrorMessage}",
            mcpSessionId,
            loginAttemptId,
            errorMessage);
    }

    private static string ToToolStatus(EntraLoginStatus status) =>
        status switch
        {
            EntraLoginStatus.SignedOut => "signed_out",
            EntraLoginStatus.PendingUserAction => "pending_user_action",
            EntraLoginStatus.Authenticated => "authenticated",
            EntraLoginStatus.Failed => "failed",
            _ => "unknown"
        };
}
