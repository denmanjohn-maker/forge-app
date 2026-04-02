using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MtgDeckForge.Api.Models;

namespace MtgDeckForge.Api.Services;

public class DeckService
{
    private readonly IMongoCollection<DeckConfiguration> _decksCollection;

    public DeckService(IOptions<MongoDbSettings> settings)
    {
        var mongoClient = new MongoClient(settings.Value.ConnectionString);
        var mongoDatabase = mongoClient.GetDatabase(settings.Value.DatabaseName);
        _decksCollection = mongoDatabase.GetCollection<DeckConfiguration>(settings.Value.DecksCollectionName);
    }

    public async Task<List<DeckConfiguration>> GetAllAsync() =>
        await _decksCollection.Find(_ => true)
            .SortByDescending(d => d.CreatedAt)
            .ToListAsync();

    public async Task<DeckConfiguration?> GetByIdAsync(string id) =>
        await _decksCollection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task<List<DeckConfiguration>> SearchAsync(string? color = null, string? format = null)
    {
        var builder = Builders<DeckConfiguration>.Filter;
        var filter = builder.Empty;

        if (!string.IsNullOrEmpty(color))
            filter &= builder.AnyEq(d => d.Colors, color);

        if (!string.IsNullOrEmpty(format))
            filter &= builder.Eq(d => d.Format, format);

        return await _decksCollection.Find(filter)
            .SortByDescending(d => d.CreatedAt)
            .ToListAsync();
    }

    public async Task<DeckConfiguration> CreateAsync(DeckConfiguration deck)
    {
        deck.CreatedAt = DateTime.UtcNow;
        deck.UpdatedAt = DateTime.UtcNow;
        await _decksCollection.InsertOneAsync(deck);
        return deck;
    }

    public async Task<List<DeckConfiguration>> GetByUserIdAsync(string userId) =>
        await _decksCollection.Find(d => d.UserId == userId)
            .SortByDescending(d => d.CreatedAt)
            .ToListAsync();

    public async Task<bool> DeleteAsync(string id)
    {
        var result = await _decksCollection.DeleteOneAsync(x => x.Id == id);
        return result.DeletedCount > 0;
    }
}
