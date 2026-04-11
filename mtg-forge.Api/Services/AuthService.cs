using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

public class AuthService
{
    private readonly JwtSettings _jwtSettings;
    private readonly UserService _userService;

    public AuthService(IOptions<JwtSettings> jwtSettings, UserService userService)
    {
        _jwtSettings = jwtSettings.Value;
        _userService = userService;
    }

    public string HashPassword(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password);

    public bool VerifyPassword(string password, string hash) =>
        BCrypt.Net.BCrypt.Verify(password, hash);

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

    public async Task<LoginResponse?> AuthenticateAsync(string username, string password)
    {
        var user = await _userService.GetByUsernameAsync(username);
        if (user is null || !VerifyPassword(password, user.PasswordHash))
            return null;

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
