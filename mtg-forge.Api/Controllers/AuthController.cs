using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MtgForge.Api.Models;
using MtgForge.Api.Services;

namespace MtgForge.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly UserService _userService;

    public AuthController(AuthService authService, UserService userService)
    {
        _authService = authService;
        _userService = userService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        var response = await _authService.AuthenticateAsync(request.Username, request.Password);
        if (response is null)
            return Unauthorized(new { error = "Invalid username or password" });

        return Ok(response);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("register")]
    public async Task<ActionResult<UserResponse>> Register([FromBody] RegisterRequest request)
    {
        var existing = await _userService.GetByUsernameAsync(request.Username);
        if (existing is not null)
            return Conflict(new { error = "Username already exists" });

        var user = new User
        {
            Username = request.Username,
            PasswordHash = _authService.HashPassword(request.Password),
            DisplayName = request.DisplayName,
            Role = request.Role,
            GroupIds = request.GroupIds
        };

        var created = await _userService.CreateUserAsync(user);

        return CreatedAtAction(nameof(GetMe), ToUserResponse(created));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> GetMe()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userService.GetByIdAsync(userId!);
        if (user is null)
            return NotFound();

        return Ok(ToUserResponse(user));
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("users")]
    public async Task<ActionResult<List<UserResponse>>> GetAllUsers()
    {
        var users = await _userService.GetAllUsersAsync();
        return Ok(users.Select(ToUserResponse).ToList());
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("users/{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 4)
            return BadRequest(new { error = "Password must be at least 4 characters" });

        var user = await _userService.GetByIdAsync(id);
        if (user is null)
            return NotFound(new { error = "User not found" });

        user.PasswordHash = _authService.HashPassword(request.NewPassword);
        await _userService.UpdateUserAsync(id, user);

        return Ok(new { message = "Password reset successfully" });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id == currentUserId)
            return BadRequest(new { error = "Cannot delete your own account" });

        var deleted = await _userService.DeleteUserAsync(id);
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    private static UserResponse ToUserResponse(User user) => new()
    {
        Id = user.Id!,
        Username = user.Username,
        DisplayName = user.DisplayName,
        Role = user.Role,
        GroupIds = user.GroupIds,
        CreatedAt = user.CreatedAt
    };
}
