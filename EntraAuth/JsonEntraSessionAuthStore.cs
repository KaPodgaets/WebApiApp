using System.Text.Json;

namespace WebApiApp.EntraAuth;

public sealed class JsonEntraSessionAuthStore : IEntraSessionAuthStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath;
    private readonly ILogger<JsonEntraSessionAuthStore> _logger;

    public JsonEntraSessionAuthStore(
        EntraAuthOptions options,
        ILogger<JsonEntraSessionAuthStore> logger)
    {
        _filePath = options.AuthStateFilePath;
        _logger = logger;

        var directoryPath = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }

    public async Task<EntraSessionAuthState?> GetAsync(string mcpSessionId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var persisted = await ReadAllAsync(cancellationToken);
            return persisted.TryGetValue(mcpSessionId, out var value)
                ? ToRuntimeState(value)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(EntraSessionAuthState state, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var persisted = await ReadAllAsync(cancellationToken);
            state.UpdatedAtUtc = DateTimeOffset.UtcNow;
            persisted[state.McpSessionId] = ToPersistedState(state);
            await WriteAllAsync(persisted, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string mcpSessionId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var persisted = await ReadAllAsync(cancellationToken);
            if (persisted.Remove(mcpSessionId))
            {
                await WriteAllAsync(persisted, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, PersistedEntraSessionAuthState>> ReadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, PersistedEntraSessionAuthState>(StringComparer.Ordinal);
        }

        try
        {
            await using var stream = File.Open(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var persisted = await JsonSerializer.DeserializeAsync<Dictionary<string, PersistedEntraSessionAuthState>>(
                stream,
                SerializerOptions,
                cancellationToken);

            return persisted ?? new Dictionary<string, PersistedEntraSessionAuthState>(StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Microsoft Entra auth state file at {AuthStateFilePath} could not be parsed. Returning an empty in-memory state.",
                _filePath);
            return new Dictionary<string, PersistedEntraSessionAuthState>(StringComparer.Ordinal);
        }
    }

    private async Task WriteAllAsync(
        Dictionary<string, PersistedEntraSessionAuthState> persisted,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Open(_filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, persisted, SerializerOptions, cancellationToken);
    }

    private PersistedEntraSessionAuthState ToPersistedState(EntraSessionAuthState state)
    {
        return new PersistedEntraSessionAuthState
        {
            McpSessionId = state.McpSessionId,
            LoginAttemptId = state.LoginAttemptId,
            Status = state.Status,
            PowerBiSemanticModelId = state.PowerBiSemanticModelId,
            VerificationUri = state.VerificationUri,
            UserCode = state.UserCode,
            Scope = state.Scope,
            Message = state.Message,
            PollIntervalSeconds = state.PollIntervalSeconds,
            DeviceCodeExpiresAtUtc = state.DeviceCodeExpiresAtUtc,
            AccessTokenExpiresAtUtc = state.AccessTokenExpiresAtUtc,
            LastError = state.LastError,
            UpdatedAtUtc = state.UpdatedAtUtc,
            DeviceCode = state.DeviceCode,
            AccessToken = state.AccessToken,
            RefreshToken = state.RefreshToken
        };
    }

    private EntraSessionAuthState ToRuntimeState(PersistedEntraSessionAuthState state)
    {
        return new EntraSessionAuthState
        {
            McpSessionId = state.McpSessionId,
            LoginAttemptId = state.LoginAttemptId,
            Status = state.Status,
            PowerBiSemanticModelId = state.PowerBiSemanticModelId,
            VerificationUri = state.VerificationUri,
            UserCode = state.UserCode,
            Scope = state.Scope,
            Message = state.Message,
            PollIntervalSeconds = state.PollIntervalSeconds,
            DeviceCodeExpiresAtUtc = state.DeviceCodeExpiresAtUtc,
            AccessTokenExpiresAtUtc = state.AccessTokenExpiresAtUtc,
            LastError = state.LastError,
            UpdatedAtUtc = state.UpdatedAtUtc,
            DeviceCode = state.DeviceCode,
            AccessToken = state.AccessToken,
            RefreshToken = state.RefreshToken
        };
    }

    private sealed class PersistedEntraSessionAuthState
    {
        public string McpSessionId { get; init; } = string.Empty;
        public string LoginAttemptId { get; init; } = string.Empty;
        public EntraLoginStatus Status { get; init; }
        public string? PowerBiSemanticModelId { get; init; }
        public string? VerificationUri { get; init; }
        public string? UserCode { get; init; }
        public string? Scope { get; init; }
        public string? Message { get; init; }
        public int PollIntervalSeconds { get; init; }
        public DateTimeOffset? DeviceCodeExpiresAtUtc { get; init; }
        public DateTimeOffset? AccessTokenExpiresAtUtc { get; init; }
        public string? LastError { get; init; }
        public DateTimeOffset UpdatedAtUtc { get; init; }
        public string? DeviceCode { get; init; }
        public string? AccessToken { get; init; }
        public string? RefreshToken { get; init; }
    }
}
