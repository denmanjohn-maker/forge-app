using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

/// <summary>
/// Handles OAuth2 provider user lookup / creation and CSRF state cookie management.
/// <para>
/// Both Google and Discord flows use the same pattern:
/// <list type="number">
///   <item>Redirect the browser to the provider's authorization URL with a random <c>state</c> parameter.</item>
///   <item>On callback, verify the <c>state</c> matches the cookie set in step 1.</item>
///   <item>Call <see cref="FindOrCreateGoogleUserAsync"/> / <see cref="FindOrCreateDiscordUserAsync"/>.</item>
///   <item>Issue a JWT via <see cref="AuthService.GenerateToken"/>.</item>
/// </list>
/// </para>
/// </summary>
public class OAuthService
{
    private const string StateCookieName = "oauth_state";
    private const int StateExpiryMinutes = 10;

    private readonly UserService _userService;
    private readonly AuthService _authService;

    public OAuthService(UserService userService, AuthService authService)
    {
        _userService = userService;
        _authService = authService;
    }

    // ── CSRF state helpers ────────────────────────────────────────────────

    /// <summary>
    /// Generates a cryptographically random state string, stores it in an
    /// HTTP-only cookie on <paramref name="response"/>, and returns it for
    /// inclusion in the provider authorization URL.
    /// </summary>
    public string GenerateState(HttpResponse response, bool isHttps)
    {
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

        response.Cookies.Append(StateCookieName, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(StateExpiryMinutes)
        });

        return state;
    }

    /// <summary>
    /// Reads the state from the <paramref name="request"/> cookie, compares it
    /// with <paramref name="returnedState"/>, and deletes the cookie.
    /// Returns <c>true</c> if the states match; <c>false</c> otherwise.
    /// </summary>
    public bool ValidateState(HttpRequest request, HttpResponse response, string? returnedState)
    {
        var stored = request.Cookies[StateCookieName];
        response.Cookies.Delete(StateCookieName);

        return !string.IsNullOrEmpty(stored)
            && !string.IsNullOrEmpty(returnedState)
            && stored == returnedState;
    }

    // ── Google ────────────────────────────────────────────────────────────

    /// <summary>
    /// Looks up an existing user by <paramref name="googleId"/>. If not found,
    /// creates a new <c>User</c> document with role <c>User</c> and no password hash.
    /// Returns a signed JWT for the resolved user.
    /// </summary>
    public async Task<string> FindOrCreateGoogleUserAsync(
        string googleId,
        string email,
        string displayName,
        string? avatarUrl)
    {
        var user = await _userService.GetByGoogleIdAsync(googleId);

        if (user is null)
        {
            // Check for an existing local account with the same email so we
            // link rather than duplicate.
            user = await _userService.GetByUsernameAsync(email);
            if (user is not null)
            {
                // Link the Google ID to the existing account.
                user.GoogleId = googleId;
                if (string.IsNullOrEmpty(user.AvatarUrl) && avatarUrl is not null)
                    user.AvatarUrl = avatarUrl;
                await _userService.UpdateUserAsync(user.Id!, user);
            }
            else
            {
                user = new User
                {
                    Username = email,
                    DisplayName = displayName,
                    Role = "User",
                    GoogleId = googleId,
                    AvatarUrl = avatarUrl
                };
                await _userService.CreateUserAsync(user);
            }
        }

        await _userService.UpdateLastLoginAsync(user.Id!);
        return _authService.GenerateToken(user);
    }

    // ── Discord ───────────────────────────────────────────────────────────

    /// <summary>
    /// Looks up an existing user by <paramref name="discordId"/>. If not found,
    /// creates a new <c>User</c> document with role <c>User</c> and no password hash.
    /// Returns a signed JWT for the resolved user.
    /// </summary>
    public async Task<string> FindOrCreateDiscordUserAsync(
        string discordId,
        string username,
        string displayName,
        string? avatarUrl)
    {
        var user = await _userService.GetByDiscordIdAsync(discordId);

        if (user is null)
        {
            // Check for an existing local account with the same username.
            user = await _userService.GetByUsernameAsync(username);
            if (user is not null)
            {
                user.DiscordId = discordId;
                if (string.IsNullOrEmpty(user.AvatarUrl) && avatarUrl is not null)
                    user.AvatarUrl = avatarUrl;
                await _userService.UpdateUserAsync(user.Id!, user);
            }
            else
            {
                user = new User
                {
                    Username = username,
                    DisplayName = displayName,
                    Role = "User",
                    DiscordId = discordId,
                    AvatarUrl = avatarUrl
                };
                await _userService.CreateUserAsync(user);
            }
        }

        await _userService.UpdateLastLoginAsync(user.Id!);
        return _authService.GenerateToken(user);
    }
}
