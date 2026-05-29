using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

/// <summary>
/// Handles password hashing, JWT token generation, and credential verification
/// for the API's custom auth flow (separate from ASP.NET Identity, which is only
/// used for the Razor Pages cookie flow).
/// </summary>
public class AuthService
{
    private readonly JwtSettings _jwtSettings;
    private readonly UserService _userService;

    public AuthService(IOptions<JwtSettings> jwtSettings, UserService userService)
    {
        _jwtSettings = jwtSettings.Value;
        _userService = userService;
    }

    /// <summary>Returns a BCrypt hash of <paramref name="password"/>.</summary>
    public string HashPassword(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password);

    /// <summary>Verifies a plain-text <paramref name="password"/> against a stored BCrypt <paramref name="hash"/>.
    /// Returns <c>false</c> if <paramref name="hash"/> is null (e.g. OAuth-only accounts have no password).</summary>
    public bool VerifyPassword(string password, string? hash) =>
        hash is not null && BCrypt.Net.BCrypt.Verify(password, hash);

    /// <summary>
    /// Creates a signed JWT bearer token for <paramref name="user"/>, embedding
    /// the user ID, username, role, and display name as claims.
    /// </summary>
    public string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id!),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("displayName", user.DisplayName)
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Looks up the user by <paramref name="username"/>, verifies their password, updates
    /// the last-login timestamp, and returns a <see cref="LoginResponse"/> containing a
    /// fresh JWT. Returns <c>null</c> if the credentials are invalid.
    /// </summary>
    public async Task<LoginResponse?> AuthenticateAsync(string username, string password)
    {
        var user = await _userService.GetByUsernameAsync(username);
        if (user is null || !VerifyPassword(password, user.PasswordHash))
            return null;

        await _userService.UpdateLastLoginAsync(user.Id!);
        var token = GenerateToken(user);

        return new LoginResponse
        {
            Token = token,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes)
        };
    }
}
