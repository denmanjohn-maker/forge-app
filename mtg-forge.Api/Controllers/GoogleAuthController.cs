using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MtgForge.Api.Models;
using MtgForge.Api.Services;

namespace MtgForge.Api.Controllers;

/// <summary>
/// Implements the Google OAuth2 authorization-code flow.
/// <para>
/// <c>GET /api/auth/google</c> — redirects the browser to Google's consent screen.
/// <c>GET /api/auth/google/callback</c> — exchanges the authorization code for a JWT,
/// then redirects to the SPA's <c>/?oauth_token=&lt;jwt&gt;</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/auth/google")]
public class GoogleAuthController : ControllerBase
{
    private readonly OAuthSettings _settings;
    private readonly OAuthService _oauthService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GoogleAuthController> _logger;

    public GoogleAuthController(
        IOptions<OAuthSettings> settings,
        OAuthService oauthService,
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleAuthController> logger)
    {
        _settings = settings.Value;
        _oauthService = oauthService;
        _httpClient = httpClientFactory.CreateClient("Google");
        _logger = logger;
    }

    /// <summary>
    /// Initiates the Google OAuth2 flow. Builds the authorization URL, stores a
    /// CSRF state cookie, and redirects the browser to Google's consent screen.
    /// </summary>
    [HttpGet]
    public IActionResult Initiate()
    {
        if (string.IsNullOrEmpty(_settings.Google.ClientId))
            return BadRequest(new { error = "Google OAuth is not configured." });

        var state = _oauthService.GenerateState(Response, Request.IsHttps);

        var query = new Dictionary<string, string>
        {
            ["client_id"] = _settings.Google.ClientId,
            ["redirect_uri"] = _settings.Google.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["state"] = state,
            ["access_type"] = "online"
        };

        var url = "https://accounts.google.com/o/oauth2/v2/auth?" +
                  string.Join("&", query.Select(kv =>
                      $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return Redirect(url);
    }

    /// <summary>
    /// Handles the redirect from Google after the user grants (or denies) consent.
    /// Exchanges the authorization code for an access token, fetches the user's
    /// Google profile, and issues a site JWT before redirecting to the SPA.
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error)
    {
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Google OAuth denied by user: {Error}", error);
            return Redirect($"/?oauth_error={Uri.EscapeDataString("Google sign-in was cancelled.")}");
        }

        if (!_oauthService.ValidateState(Request, Response, state))
        {
            _logger.LogWarning("Google OAuth CSRF state mismatch");
            return Redirect($"/?oauth_error={Uri.EscapeDataString("Invalid OAuth state. Please try again.")}");
        }

        if (string.IsNullOrEmpty(code))
        {
            return Redirect($"/?oauth_error={Uri.EscapeDataString("No authorization code received from Google.")}");
        }

        try
        {
            // Exchange code for tokens
            var tokenResponse = await ExchangeCodeAsync(code);
            if (tokenResponse is null)
                return Redirect($"/?oauth_error={Uri.EscapeDataString("Failed to exchange authorization code with Google.")}");

            // Fetch user profile
            var profile = await FetchUserProfileAsync(tokenResponse.AccessToken);
            if (profile is null)
                return Redirect($"/?oauth_error={Uri.EscapeDataString("Failed to retrieve Google profile.")}");

            var jwt = await _oauthService.FindOrCreateGoogleUserAsync(
                profile.Id,
                profile.Email,
                profile.Name,
                profile.Picture);

            return Redirect($"/?oauth_token={Uri.EscapeDataString(jwt)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during Google OAuth callback");
            return Redirect($"/?oauth_error={Uri.EscapeDataString("An unexpected error occurred. Please try again.")}");
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private async Task<GoogleTokenResponse?> ExchangeCodeAsync(string code)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _settings.Google.ClientId,
            ["client_secret"] = _settings.Google.ClientSecret,
            ["redirect_uri"] = _settings.Google.RedirectUri,
            ["grant_type"] = "authorization_code"
        });

        var response = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", body);
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Google token exchange failed: {StatusCode} {Body}",
                response.StatusCode, content);
            return null;
        }

        using var json = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<GoogleTokenResponse>(json);
    }

    private async Task<GoogleUserProfile?> FetchUserProfileAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://www.googleapis.com/oauth2/v2/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Google userinfo fetch failed: {StatusCode}", response.StatusCode);
            return null;
        }

        using var json = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<GoogleUserProfile>(json);
    }

    // ── Response models (private, only used in this controller) ──────────

    private sealed record GoogleTokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")]
        string AccessToken);

    private sealed record GoogleUserProfile(
        [property: System.Text.Json.Serialization.JsonPropertyName("id")]
        string Id,
        [property: System.Text.Json.Serialization.JsonPropertyName("email")]
        string Email,
        [property: System.Text.Json.Serialization.JsonPropertyName("name")]
        string Name,
        [property: System.Text.Json.Serialization.JsonPropertyName("picture")]
        string? Picture);
}
