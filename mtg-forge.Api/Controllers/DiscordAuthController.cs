using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MtgForge.Api.Models;
using MtgForge.Api.Services;

namespace MtgForge.Api.Controllers;

/// <summary>
/// Implements the Discord OAuth2 authorization-code flow.
/// <para>
/// <c>GET /api/auth/discord</c> — redirects the browser to Discord's consent screen.
/// <c>GET /api/auth/discord/callback</c> — exchanges the authorization code for a JWT,
/// then redirects to the SPA's <c>/?oauth_token=&lt;jwt&gt;</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/auth/discord")]
public class DiscordAuthController : ControllerBase
{
    private readonly OAuthSettings _settings;
    private readonly OAuthService _oauthService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<DiscordAuthController> _logger;

    public DiscordAuthController(
        IOptions<OAuthSettings> settings,
        OAuthService oauthService,
        IHttpClientFactory httpClientFactory,
        ILogger<DiscordAuthController> logger)
    {
        _settings = settings.Value;
        _oauthService = oauthService;
        _httpClient = httpClientFactory.CreateClient("Discord");
        _logger = logger;
    }

    /// <summary>
    /// Initiates the Discord OAuth2 flow. Builds the authorization URL, stores a
    /// CSRF state cookie, and redirects the browser to Discord's consent screen.
    /// </summary>
    [HttpGet]
    public IActionResult Initiate()
    {
        if (string.IsNullOrEmpty(_settings.Discord.ClientId))
            return BadRequest(new { error = "Discord OAuth is not configured." });

        var state = _oauthService.GenerateState(Response, Request.IsHttps);

        var query = new Dictionary<string, string>
        {
            ["client_id"] = _settings.Discord.ClientId,
            ["redirect_uri"] = _settings.Discord.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "identify email",
            ["state"] = state
        };

        var url = "https://discord.com/oauth2/authorize?" +
                  string.Join("&", query.Select(kv =>
                      $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return Redirect(url);
    }

    /// <summary>
    /// Handles the redirect from Discord after the user grants (or denies) consent.
    /// Exchanges the authorization code for an access token, fetches the user's
    /// Discord profile, and issues a site JWT before redirecting to the SPA.
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error)
    {
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Discord OAuth denied by user: {Error}", error);
            return Redirect($"/?oauth_error={Uri.EscapeDataString("Discord sign-in was cancelled.")}");
        }

        if (!_oauthService.ValidateState(Request, Response, state))
        {
            _logger.LogWarning("Discord OAuth CSRF state mismatch");
            return Redirect($"/?oauth_error={Uri.EscapeDataString("Invalid OAuth state. Please try again.")}");
        }

        if (string.IsNullOrEmpty(code))
        {
            return Redirect($"/?oauth_error={Uri.EscapeDataString("No authorization code received from Discord.")}");
        }

        try
        {
            // Exchange code for tokens
            var accessToken = await ExchangeCodeAsync(code);
            if (accessToken is null)
                return Redirect($"/?oauth_error={Uri.EscapeDataString("Failed to exchange authorization code with Discord.")}");

            // Fetch user profile
            var profile = await FetchUserProfileAsync(accessToken);
            if (profile is null)
                return Redirect($"/?oauth_error={Uri.EscapeDataString("Failed to retrieve Discord profile.")}");

            // Build avatar URL if Discord has one
            string? avatarUrl = profile.Avatar is not null
                ? $"https://cdn.discordapp.com/avatars/{profile.Id}/{profile.Avatar}.png"
                : null;

            // Discord's global_name is the display name; fall back to username
            var displayName = profile.GlobalName ?? profile.Username;

            var jwt = await _oauthService.FindOrCreateDiscordUserAsync(
                profile.Id,
                profile.Username,
                displayName,
                avatarUrl);

            return Redirect($"/?oauth_token={Uri.EscapeDataString(jwt)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during Discord OAuth callback");
            return Redirect($"/?oauth_error={Uri.EscapeDataString("An unexpected error occurred. Please try again.")}");
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private async Task<string?> ExchangeCodeAsync(string code)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _settings.Discord.ClientId,
            ["client_secret"] = _settings.Discord.ClientSecret,
            ["redirect_uri"] = _settings.Discord.RedirectUri,
            ["grant_type"] = "authorization_code"
        });

        var response = await _httpClient.PostAsync("https://discord.com/api/oauth2/token", body);
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Discord token exchange failed: {StatusCode} {Body}",
                response.StatusCode, content);
            return null;
        }

        using var json = await response.Content.ReadAsStreamAsync();
        var tokenResponse = await JsonSerializer.DeserializeAsync<DiscordTokenResponse>(json);
        return tokenResponse?.AccessToken;
    }

    private async Task<DiscordUserProfile?> FetchUserProfileAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://discord.com/api/users/@me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Discord user fetch failed: {StatusCode}", response.StatusCode);
            return null;
        }

        using var json = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<DiscordUserProfile>(json);
    }

    // ── Response models (private, only used in this controller) ──────────

    private sealed record DiscordTokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")]
        string AccessToken);

    private sealed record DiscordUserProfile(
        [property: System.Text.Json.Serialization.JsonPropertyName("id")]
        string Id,
        [property: System.Text.Json.Serialization.JsonPropertyName("username")]
        string Username,
        [property: System.Text.Json.Serialization.JsonPropertyName("global_name")]
        string? GlobalName,
        [property: System.Text.Json.Serialization.JsonPropertyName("avatar")]
        string? Avatar);
}
