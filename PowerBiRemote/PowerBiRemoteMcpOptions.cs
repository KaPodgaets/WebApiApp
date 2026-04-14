using System.Text.Json;

namespace WebApiApp.PowerBiRemote;

public sealed record PowerBiRemoteMcpOptions(
    string EndpointUrl,
    string ProtocolVersion)
{
    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(EndpointUrl))
        {
            throw new InvalidOperationException(
                "PowerBiRemoteMcp:EndpointUrl is not configured. Add it to appsettings.");
        }

        if (string.IsNullOrWhiteSpace(ProtocolVersion))
        {
            throw new InvalidOperationException(
                "PowerBiRemoteMcp:ProtocolVersion is not configured. Add it to appsettings.");
        }
    }
}

public sealed record PowerBiRemoteToolDefinition(
    string Name,
    string? Description,
    JsonElement InputSchema);
