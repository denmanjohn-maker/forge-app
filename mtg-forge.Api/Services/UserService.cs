using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

/// <summary>
/// Provides CRUD operations for <see cref="User"/> and <see cref="Group"/> documents
/// in MongoDB. Also handles admin-user seeding on startup.
/// <para>
/// This service handles the API auth users (stored in MongoDB).
/// ASP.NET Identity's <c>ApplicationUser</c> is used separately, only for the
/// Razor Pages cookie-based flow.
/// </para>
/// </summary>
public class UserService
{
    private readonly IMongoCollection<User> _usersCollection;
    private readonly IMongoCollection<Group> _groupsCollection;

    public UserService(IOptions<MongoDbSettings> settings)
    {
        var mongoClient = new MongoClient(settings.Value.ConnectionString);
        var mongoDatabase = mongoClient.GetDatabase(settings.Value.DatabaseName);
        _usersCollection = mongoDatabase.GetCollection<User>(settings.Value.UsersCollectionName);
        _groupsCollection = mongoDatabase.GetCollection<Group>(settings.Value.GroupsCollectionName);
    }

    // === Users ===

    /// <summary>Finds a user by username (case-insensitive). Returns <c>null</c> if not found.</summary>
    public async Task<User?> GetByUsernameAsync(string username) =>
        await _usersCollection.Find(u => u.Username.ToLower() == username.ToLower()).FirstOrDefaultAsync();

    /// <summary>Finds a user by their MongoDB ObjectId string. Returns <c>null</c> if not found.</summary>
    public async Task<User?> GetByIdAsync(string id) =>
        await _usersCollection.Find(u => u.Id == id).FirstOrDefaultAsync();

    /// <summary>Returns all users, sorted newest-first.</summary>
    public async Task<List<User>> GetAllUsersAsync() =>
        await _usersCollection.Find(_ => true)
            .SortByDescending(u => u.CreatedAt)
            .ToListAsync();

    /// <summary>Inserts a new user, setting <c>CreatedAt</c> and <c>UpdatedAt</c> to the current UTC time.</summary>
    public async Task<User> CreateUserAsync(User user)
    {
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _usersCollection.InsertOneAsync(user);
        return user;
    }

    /// <summary>Replaces the user document with the provided <paramref name="user"/> value.</summary>
    public async Task<bool> UpdateUserAsync(string id, User user)
    {
        user.UpdatedAt = DateTime.UtcNow;
        var result = await _usersCollection.ReplaceOneAsync(u => u.Id == id, user);
        return result.ModifiedCount > 0;
    }

    /// <summary>Permanently deletes the user document. Returns <c>false</c> if not found.</summary>
    public async Task<bool> DeleteUserAsync(string id)
    {
        var result = await _usersCollection.DeleteOneAsync(u => u.Id == id);
        return result.DeletedCount > 0;
    }

    /// <summary>Stamps the user's <c>LastLogin</c> and <c>UpdatedAt</c> fields to the current UTC time.</summary>
    public async Task UpdateLastLoginAsync(string id)
    {
        var update = Builders<User>.Update
            .Set(u => u.LastLogin, DateTime.UtcNow)
            .Set(u => u.UpdatedAt, DateTime.UtcNow);
        await _usersCollection.UpdateOneAsync(u => u.Id == id, update);
    }

    // === Groups ===

    /// <summary>Returns all groups, sorted alphabetically by name.</summary>
    public async Task<List<Group>> GetAllGroupsAsync() =>
        await _groupsCollection.Find(_ => true)
            .SortBy(g => g.Name)
            .ToListAsync();

    /// <summary>Finds a group by its MongoDB ObjectId string. Returns <c>null</c> if not found.</summary>
    public async Task<Group?> GetGroupByIdAsync(string id) =>
        await _groupsCollection.Find(g => g.Id == id).FirstOrDefaultAsync();

    /// <summary>Inserts a new group, setting <c>CreatedAt</c> to the current UTC time.</summary>
    public async Task<Group> CreateGroupAsync(Group group)
    {
        group.CreatedAt = DateTime.UtcNow;
        await _groupsCollection.InsertOneAsync(group);
        return group;
    }

    /// <summary>Permanently deletes the group document. Returns <c>false</c> if not found.</summary>
    public async Task<bool> DeleteGroupAsync(string id)
    {
        var result = await _groupsCollection.DeleteOneAsync(g => g.Id == id);
        return result.DeletedCount > 0;
    }

    // === Setup ===

    /// <summary>Creates a unique index on <c>Username</c> if it doesn't already exist.</summary>
    public async Task EnsureIndexesAsync()
    {
        var indexModel = new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.Username),
            new CreateIndexOptions { Unique = true });
        await _usersCollection.Indexes.CreateOneAsync(indexModel);
    }

    /// <summary>
    /// Ensures an admin user exists, creating one if necessary. If the user already
    /// exists but their password hash differs (e.g. the env var was rotated), the hash
    /// is updated so the new password takes effect on the next startup.
    /// </summary>
    public async Task SeedAdminUserAsync(string passwordHash, string username = "admin", string displayName = "Administrator")
    {
        var existing = await GetByUsernameAsync(username);
        if (existing is not null)
        {
            // Update password hash if it has changed (e.g. was seeded with empty password)
            if (existing.PasswordHash != passwordHash)
            {
                existing.PasswordHash = passwordHash;
                await UpdateUserAsync(existing.Id!, existing);
            }
            return;
        }

        var admin = new User
        {
            Username = username,
            PasswordHash = passwordHash,
            DisplayName = displayName,
            Role = "Admin",
            GroupIds = new List<string>()
        };

        await CreateUserAsync(admin);
    }
}
