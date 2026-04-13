namespace WebApiApp.EntraAuth;

public interface IEntraSessionAuthStore
{
    Task<EntraSessionAuthState?> GetAsync(string mcpSessionId, CancellationToken cancellationToken);
    Task SaveAsync(EntraSessionAuthState state, CancellationToken cancellationToken);
    Task RemoveAsync(string mcpSessionId, CancellationToken cancellationToken);
}
