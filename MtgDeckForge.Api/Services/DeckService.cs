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
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        var indexes = new List<CreateIndexModel<DeckConfiguration>>
        {
            new(Builders<DeckConfiguration>.IndexKeys.Ascending(d => d.UserId)),
            new(Builders<DeckConfiguration>.IndexKeys.Ascending(d => d.Format)),
            new(Builders<DeckConfiguration>.IndexKeys.Ascending(d => d.Colors)),
            new(Builders<DeckConfiguration>.IndexKeys.Descending(d => d.CreatedAt)),
        };
        _decksCollection.Indexes.CreateMany(indexes);
    }

    public async Task<List<DeckConfiguration>> GetAllAsync() =>
        await _decksCollection.Find(_ => true)
            .SortByDescending(d => d.CreatedAt)
            .ToListAsync();

    public async Task<PagedResult<DeckConfiguration>> GetPagedAsync(
        string? userId, bool isAdmin,
        string? name, string? color, string? format, string? powerLevel,
        int skip, int limit)
    {
        var builder = Builders<DeckConfiguration>.Filter;
        var filter = builder.Empty;

        if (!isAdmin && userId != null)
            filter &= builder.Eq(d => d.UserId, userId);

        if (!string.IsNullOrEmpty(name))
            filter &= builder.Regex(d => d.DeckName, new MongoDB.Bson.BsonRegularExpression(name, "i"));

        if (!string.IsNullOrEmpty(color))
            filter &= builder.AnyEq(d => d.Colors, color);

        if (!string.IsNullOrEmpty(format))
            filter &= builder.Eq(d => d.Format, format);

        if (!string.IsNullOrEmpty(powerLevel))
            filter &= builder.Eq(d => d.PowerLevel, powerLevel);

        var total = (int)await _decksCollection.CountDocumentsAsync(filter);
        var items = await _decksCollection.Find(filter)
            .SortByDescending(d => d.CreatedAt)
            .Skip(skip)
            .Limit(limit)
            .ToListAsync();

        return new PagedResult<DeckConfiguration>
        {
            Items = items,
            Total = total,
            Skip = skip,
            Limit = limit
        };
    }

    public async Task<DeckConfiguration?> GetByIdAsync(string id) =>
        await _decksCollection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task<List<DeckConfiguration>> SearchAsync(string? color = null, string? format = null, string? userId = null)
    {
        var builder = Builders<DeckConfiguration>.Filter;
        var filter = builder.Empty;

        if (!string.IsNullOrEmpty(userId))
            filter &= builder.Eq(d => d.UserId, userId);

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

    public async Task<bool> UpdateAsync(string id, DeckUpdateRequest req)
    {
        var updates = new List<UpdateDefinition<DeckConfiguration>>();
        var builder = Builders<DeckConfiguration>.Update;

        if (req.DeckName != null)        updates.Add(builder.Set(d => d.DeckName, req.DeckName));
        if (req.Commander != null)       updates.Add(builder.Set(d => d.Commander, req.Commander));
        if (req.Strategy != null)        updates.Add(builder.Set(d => d.Strategy, req.Strategy));
        if (req.DeckDescription != null) updates.Add(builder.Set(d => d.DeckDescription, req.DeckDescription));
        if (req.Format != null)          updates.Add(builder.Set(d => d.Format, req.Format));
        if (req.PowerLevel != null)      updates.Add(builder.Set(d => d.PowerLevel, req.PowerLevel));
        if (req.BudgetRange != null)     updates.Add(builder.Set(d => d.BudgetRange, req.BudgetRange));
        if (req.Colors != null)          updates.Add(builder.Set(d => d.Colors, req.Colors));
        if (req.Cards != null)
        {
            updates.Add(builder.Set(d => d.Cards, req.Cards));
            updates.Add(builder.Set(d => d.TotalCards, req.Cards.Sum(c => c.Quantity)));
            updates.Add(builder.Set(d => d.EstimatedTotalPrice, req.Cards.Sum(c => c.EstimatedPrice * c.Quantity)));
        }

        updates.Add(builder.Set(d => d.UpdatedAt, DateTime.UtcNow));

        if (updates.Count == 0) return true;

        var result = await _decksCollection.UpdateOneAsync(
            d => d.Id == id,
            builder.Combine(updates));
        return result.ModifiedCount > 0;
    }

    public async Task<bool> UpdateAnalysisAsync(string id, DeckAnalysis analysis)
    {
        var update = Builders<DeckConfiguration>.Update
            .Set(d => d.LastAnalysis, analysis)
            .Set(d => d.LastAnalyzedAt, DateTime.UtcNow)
            .Set(d => d.UpdatedAt, DateTime.UtcNow);

        var result = await _decksCollection.UpdateOneAsync(d => d.Id == id, update);
        return result.ModifiedCount > 0;
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
