using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebApiApp.EntraAuth;

public sealed class EntraDeviceFlowClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly EntraAuthOptions _options;

    public EntraDeviceFlowClient(HttpClient httpClient, EntraAuthOptions options)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _options = options;
    }

    public async Task<EntraDeviceCodeStartResponse> StartDeviceFlowAsync(
        string scope,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.DeviceCodeEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientAppId,
                ["scope"] = scope
            })
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await ReadPayloadAsync<DeviceCodeResponsePayload>(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Microsoft Entra device-flow start failed: {payload.ErrorDescription ?? payload.Error ?? response.ReasonPhrase ?? "Unknown error"}");
        }

        if (string.IsNullOrWhiteSpace(payload.DeviceCode) ||
            string.IsNullOrWhiteSpace(payload.UserCode) ||
            string.IsNullOrWhiteSpace(payload.VerificationUri))
        {
            throw new InvalidOperationException("Microsoft Entra returned an incomplete device-flow response.");
        }

        var interval = payload.Interval > 0 ? payload.Interval : 5;
        var expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn > 0 ? payload.ExpiresIn : 900);

        return new EntraDeviceCodeStartResponse(
            payload.DeviceCode,
            payload.UserCode,
            payload.VerificationUri,
            payload.VerificationUriComplete,
            payload.Message,
            payload.ExpiresIn,
            interval,
            expiresAtUtc);
    }

    public async Task<EntraTokenPollResult> PollForTokenAsync(string deviceCode, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = _options.ClientAppId,
                ["device_code"] = deviceCode
            })
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var success = await ReadPayloadAsync<TokenSuccessPayload>(response, cancellationToken);
            if (string.IsNullOrWhiteSpace(success.AccessToken))
            {
                throw new InvalidOperationException("Microsoft Entra token response did not include an access token.");
            }

            var expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(success.ExpiresIn > 0 ? success.ExpiresIn : 3600);

            return EntraTokenPollResult.Success(
                success.AccessToken,
                success.RefreshToken,
                success.Scope,
                success.TokenType,
                expiresAtUtc);
        }

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            var error = await ReadPayloadAsync<TokenErrorPayload>(response, cancellationToken);
            return EntraTokenPollResult.Failure(
                error.Error ?? "unknown_error",
                error.ErrorDescription);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"Microsoft Entra token polling failed with status {(int)response.StatusCode}: {body}");
    }

    private static async Task<T> ReadPayloadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : new()
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);
        return payload ?? new T();
    }

    private sealed class DeviceCodeResponsePayload
    {
        [JsonPropertyName("device_code")]
        public string? DeviceCode { get; init; }
        [JsonPropertyName("user_code")]
        public string? UserCode { get; init; }
        [JsonPropertyName("verification_uri")]
        public string? VerificationUri { get; init; }
        [JsonPropertyName("verification_uri_complete")]
        public string? VerificationUriComplete { get; init; }
        public string? Message { get; init; }
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
        public int Interval { get; init; }
        public string? Error { get; init; }
        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; init; }
    }

    private sealed class TokenSuccessPayload
    {
        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }
        public string? Scope { get; init; }
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }
    }

    private sealed class TokenErrorPayload
    {
        public string? Error { get; init; }
        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; init; }
    }
}

public sealed record EntraDeviceCodeStartResponse(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string? VerificationUriComplete,
    string? Message,
    int ExpiresInSeconds,
    int PollIntervalSeconds,
    DateTimeOffset ExpiresAtUtc);

public sealed record EntraTokenPollResult(
    bool IsSuccess,
    string? Error,
    string? ErrorDescription,
    string? AccessToken,
    string? RefreshToken,
    string? Scope,
    string? TokenType,
    DateTimeOffset? AccessTokenExpiresAtUtc)
{
    public static EntraTokenPollResult Success(
        string accessToken,
        string? refreshToken,
        string? scope,
        string? tokenType,
        DateTimeOffset accessTokenExpiresAtUtc) =>
        new(
            true,
            null,
            null,
            accessToken,
            refreshToken,
            scope,
            tokenType,
            accessTokenExpiresAtUtc);

    public static EntraTokenPollResult Failure(string error, string? errorDescription) =>
        new(
            false,
            error,
            errorDescription,
            null,
            null,
            null,
            null,
            null);
}
