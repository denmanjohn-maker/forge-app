namespace MtgForge.Api.Models;

/// <summary>
/// OAuth2 credentials for a single provider (Google, Discord, etc.).
/// Set <c>ClientSecret</c> only via environment variables in production —
/// never commit a real secret to source control.
/// </summary>
public class OAuthProviderSettings
{
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Must be supplied via the environment variable
    /// <c>OAUTH__{PROVIDER}__CLIENTSECRET</c> on Railway.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;
}

/// <summary>Top-level config section <c>OAuth</c> in appsettings.json / env vars.</summary>
public class OAuthSettings
{
    public OAuthProviderSettings Google { get; set; } = new();
    public OAuthProviderSettings Discord { get; set; } = new();
}
