using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MtgForge.Api.Models;

/// <summary>
/// MongoDB document representing a user of the API auth system. This is separate
/// from ASP.NET Identity's <c>ApplicationUser</c>, which is only used for the
/// Razor Pages cookie flow.
/// </summary>
public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string Username { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Role { get; set; } = "User";
    public List<string> GroupIds { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLogin { get; set; }
}

/// <summary>
/// Named group that users can belong to. Used for organizing users in the admin
/// dashboard; does not currently grant any additional permissions.
/// </summary>
public class Group
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string Name { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Payload for <c>POST /api/auth/login</c>.</summary>
public class LoginRequest
{
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
}

/// <summary>Response body from <c>POST /api/auth/login</c>, containing the JWT bearer token.</summary>
public class LoginResponse
{
    public string Token { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Role { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
}

/// <summary>Payload for the admin-only <c>POST /api/auth/register</c> endpoint.</summary>
public class RegisterRequest
{
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Role { get; set; } = "User";
    public List<string> GroupIds { get; set; } = new();
}

/// <summary>
/// Public-facing user representation returned by list and profile endpoints.
/// Omits the password hash.
/// </summary>
public class UserResponse
{
    public string Id { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Role { get; set; } = null!;
    public List<string> GroupIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }
}

/// <summary>Payload for the admin-only <c>POST /api/auth/users/{id}/reset-password</c> endpoint.</summary>
public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = null!;
}
