namespace WebApiApp.EntraAuth;

public sealed record EntraAuthOptions(
    string ClientAppId,
    string TenantId,
    string DefaultScope,
    string AuthorityHost,
    string AuthStateFilePath)
{
    public string DeviceCodeEndpoint =>
        $"{AuthorityHost.TrimEnd('/')}/{TenantId}/oauth2/v2.0/devicecode";

    public string TokenEndpoint =>
        $"{AuthorityHost.TrimEnd('/')}/{TenantId}/oauth2/v2.0/token";

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ClientAppId))
        {
            throw new InvalidOperationException(
                "CLIENT_APP_ID is not configured. Add it to configuration or the .env file.");
        }

        if (string.IsNullOrWhiteSpace(TenantId))
        {
            throw new InvalidOperationException(
                "ENTRA_TENANT_ID is not configured. Add it to configuration or the .env file.");
        }
    }
}
