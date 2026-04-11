using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MtgForge.Api.Models;

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
}

public class Group
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string Name { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class LoginRequest
{
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
}

public class LoginResponse
{
    public string Token { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Role { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
}

public class RegisterRequest
{
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Role { get; set; } = "User";
    public List<string> GroupIds { get; set; } = new();
}

public class UserResponse
{
    public string Id { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Role { get; set; } = null!;
    public List<string> GroupIds { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = null!;
}
