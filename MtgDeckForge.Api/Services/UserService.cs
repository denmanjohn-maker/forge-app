using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MtgDeckForge.Api.Models;

namespace MtgDeckForge.Api.Services;

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

    public async Task<User?> GetByUsernameAsync(string username) =>
        await _usersCollection.Find(u => u.Username.ToLower() == username.ToLower()).FirstOrDefaultAsync();

    public async Task<User?> GetByIdAsync(string id) =>
        await _usersCollection.Find(u => u.Id == id).FirstOrDefaultAsync();

    public async Task<List<User>> GetAllUsersAsync() =>
        await _usersCollection.Find(_ => true)
            .SortByDescending(u => u.CreatedAt)
            .ToListAsync();

    public async Task<User> CreateUserAsync(User user)
    {
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _usersCollection.InsertOneAsync(user);
        return user;
    }

    public async Task<bool> UpdateUserAsync(string id, User user)
    {
        user.UpdatedAt = DateTime.UtcNow;
        var result = await _usersCollection.ReplaceOneAsync(u => u.Id == id, user);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteUserAsync(string id)
    {
        var result = await _usersCollection.DeleteOneAsync(u => u.Id == id);
        return result.DeletedCount > 0;
    }

    // === Groups ===

    public async Task<List<Group>> GetAllGroupsAsync() =>
        await _groupsCollection.Find(_ => true)
            .SortBy(g => g.Name)
            .ToListAsync();

    public async Task<Group?> GetGroupByIdAsync(string id) =>
        await _groupsCollection.Find(g => g.Id == id).FirstOrDefaultAsync();

    public async Task<Group> CreateGroupAsync(Group group)
    {
        group.CreatedAt = DateTime.UtcNow;
        await _groupsCollection.InsertOneAsync(group);
        return group;
    }

    public async Task<bool> DeleteGroupAsync(string id)
    {
        var result = await _groupsCollection.DeleteOneAsync(g => g.Id == id);
        return result.DeletedCount > 0;
    }

    // === Setup ===

    public async Task EnsureIndexesAsync()
    {
        var indexModel = new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.Username),
            new CreateIndexOptions { Unique = true });
        await _usersCollection.Indexes.CreateOneAsync(indexModel);
    }

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
