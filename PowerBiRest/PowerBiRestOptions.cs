namespace WebApiApp.PowerBiRest;

public sealed record PowerBiRestOptions(string BaseUrl)
{
    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new InvalidOperationException(
                "PowerBiRest:BaseUrl is not configured. Add it to appsettings.");
        }
    }
}
