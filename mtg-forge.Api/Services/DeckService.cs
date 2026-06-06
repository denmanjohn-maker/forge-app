using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

/// <summary>
/// Provides CRUD, search, and analytics operations for <see cref="DeckConfiguration"/>
/// documents stored in MongoDB, and maintains a companion history collection that records
/// card changes made to each deck.
/// </summary>
public class DeckService
{
    private readonly IMongoCollection<DeckConfiguration> _decksCollection;
    private readonly IMongoCollection<DeckHistoryEntry> _historyCollection;

    public DeckService(IOptions<MongoDbSettings> settings)
    {
        var mongoClient = new MongoClient(settings.Value.ConnectionString);
        var mongoDatabase = mongoClient.GetDatabase(settings.Value.DatabaseName);
        _decksCollection = mongoDatabase.GetCollection<DeckConfiguration>(settings.Value.DecksCollectionName);
        _historyCollection = mongoDatabase.GetCollection<DeckHistoryEntry>(settings.Value.DeckHistoryCollectionName);
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

        _historyCollection.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<DeckHistoryEntry>(
                Builders<DeckHistoryEntry>.IndexKeys
                    .Ascending(h => h.DeckId)
                    .Descending(h => h.Timestamp))
        });
    }

    /// <summary>Returns all decks across all users, sorted newest-first. Prefer <see cref="GetPagedAsync"/> for user-facing calls.</summary>
    public async Task<List<DeckConfiguration>> GetAllAsync() =>
        await _decksCollection.Find(_ => true)
            .SortByDescending(d => d.CreatedAt)
            .ToListAsync();

    /// <summary>
    /// Returns a page of decks filtered by user, name, color, format, and power level.
    /// When <paramref name="isAdmin"/> is <c>true</c>, decks from all users are included.
    /// </summary>
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

    /// <summary>Finds a single deck by its MongoDB ObjectId string. Returns <c>null</c> if not found.</summary>
    public async Task<DeckConfiguration?> GetByIdAsync(string id) =>
        await _decksCollection.Find(x => x.Id == id).FirstOrDefaultAsync();

    /// <summary>
    /// Returns decks matching optional color, format, and/or user-ID filters, sorted
    /// newest-first. All filters are ANDed together.
    /// </summary>
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

    /// <summary>
    /// Inserts a new deck, setting <c>CreatedAt</c> and <c>UpdatedAt</c> to the current
    /// UTC time. The MongoDB-assigned <c>Id</c> is written back to the passed object.
    /// </summary>
    public async Task<DeckConfiguration> CreateAsync(DeckConfiguration deck)
    {
        deck.CreatedAt = DateTime.UtcNow;
        deck.UpdatedAt = DateTime.UtcNow;
        await _decksCollection.InsertOneAsync(deck);
        return deck;
    }

    /// <summary>
    /// Applies a partial update from <paramref name="req"/>, updating only the fields
    /// that are non-null. When <paramref name="req"/> includes a card list change and
    /// <paramref name="userId"/> is provided, a <see cref="DeckHistoryEntry"/> is written
    /// if the card list differs from the existing deck.
    /// Returns <c>false</c> if no document was modified.
    /// </summary>
    public async Task<bool> UpdateAsync(string id, DeckUpdateRequest req, string? userId = null)
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
        if (req.Tags != null)            updates.Add(builder.Set(d => d.Tags, req.Tags));
        if (req.IsFavorite.HasValue)     updates.Add(builder.Set(d => d.IsFavorite, req.IsFavorite.Value));
        if (req.Primer != null)          updates.Add(builder.Set(d => d.Primer, req.Primer));
        if (req.Cards != null)
        {
            // Record history entry for card changes
            var existing = await GetByIdAsync(id);
            if (existing != null && userId != null)
            {
                var oldMap = existing.Cards
                    .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Sum(c => c.Quantity), StringComparer.OrdinalIgnoreCase);
                
                var newMap = req.Cards
                    .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Sum(c => c.Quantity), StringComparer.OrdinalIgnoreCase);
                
                var added = newMap.Keys.Except(oldMap.Keys, StringComparer.OrdinalIgnoreCase).ToList();
                var removed = oldMap.Keys.Except(newMap.Keys, StringComparer.OrdinalIgnoreCase).ToList();
                var qtyChanged = newMap.Keys
                    .Where(n => oldMap.ContainsKey(n) && oldMap[n] != newMap[n])
                    .Select(n => $"{n} ({oldMap[n]}→{newMap[n]})")
                    .ToList();
                if (added.Count > 0 || removed.Count > 0 || qtyChanged.Count > 0)
                {
                    var parts = new List<string>();
                    if (added.Count > 0) parts.Add($"Added {string.Join(", ", added.Take(5))}{(added.Count > 5 ? $" +{added.Count - 5} more" : "")}");
                    if (removed.Count > 0) parts.Add($"Removed {string.Join(", ", removed.Take(5))}{(removed.Count > 5 ? $" +{removed.Count - 5} more" : "")}");
                    if (qtyChanged.Count > 0) parts.Add($"Qty changed: {string.Join(", ", qtyChanged.Take(5))}{(qtyChanged.Count > 5 ? $" +{qtyChanged.Count - 5} more" : "")}");

                    await _historyCollection.InsertOneAsync(new DeckHistoryEntry
                    {
                        DeckId = id,
                        UserId = userId,
                        Timestamp = DateTime.UtcNow,
                        ChangeSummary = string.Join(". ", parts),
                        CardsAdded = added,
                        CardsRemoved = removed
                    });
                }
            }

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

    /// <summary>
    /// Returns the most recent history entries for a deck, sorted newest-first.
    /// Defaults to 25 entries.
    /// </summary>
    public async Task<List<DeckHistoryEntry>> GetHistoryAsync(string deckId, int limit = 25)
    {
        return await _historyCollection
            .Find(h => h.DeckId == deckId)
            .SortByDescending(h => h.Timestamp)
            .Limit(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Persists a completed <see cref="DeckAnalysis"/> to the deck document and stamps
    /// <c>LastAnalyzedAt</c> to the current UTC time.
    /// </summary>
    public async Task<bool> UpdateAnalysisAsync(string id, DeckAnalysis analysis)
    {
        var deckFilter = Builders<DeckConfiguration>.Filter.Eq(d => d.Id, id);
        var update = Builders<DeckConfiguration>.Update
            .Set(d => d.LastAnalysis, analysis)
            .Set(d => d.LastAnalyzedAt, DateTime.UtcNow)
            .Set(d => d.UpdatedAt, DateTime.UtcNow);

        if (!string.IsNullOrWhiteSpace(analysis.Primer))
        {
            var missingPrimerFilter = Builders<DeckConfiguration>.Filter.And(
                deckFilter,
                Builders<DeckConfiguration>.Filter.Or(
                    Builders<DeckConfiguration>.Filter.Eq(d => d.Primer, null),
                    Builders<DeckConfiguration>.Filter.Eq(d => d.Primer, string.Empty)));

            var resultWithPrimer = await _decksCollection.UpdateOneAsync(
                missingPrimerFilter,
                update.Set(d => d.Primer, analysis.Primer));

            if (resultWithPrimer.ModifiedCount > 0)
            {
                return true;
            }
        }

        var result = await _decksCollection.UpdateOneAsync(deckFilter, update);
        return result.ModifiedCount > 0;
    }

    /// <summary>Returns all decks owned by <paramref name="userId"/>, sorted newest-first.</summary>
    public async Task<List<DeckConfiguration>> GetByUserIdAsync(string userId) =>
        await _decksCollection.Find(d => d.UserId == userId)
            .SortByDescending(d => d.CreatedAt)
            .ToListAsync();

    /// <summary>Permanently deletes the deck document. Returns <c>false</c> if not found.</summary>
    public async Task<bool> DeleteAsync(string id)
    {
        var result = await _decksCollection.DeleteOneAsync(x => x.Id == id);
        return result.DeletedCount > 0;
    }

    /// <summary>
    /// Returns decks whose analysis is stale (missing or older than cutoff).
    /// Uses a server-side filter to avoid loading the full collection into memory.
    /// </summary>
    public async Task<List<DeckConfiguration>> GetStaleDecksAsync(DateTime cutoff, int limit)
    {
        var filter = Builders<DeckConfiguration>.Filter.Or(
            Builders<DeckConfiguration>.Filter.Exists(d => d.LastAnalysis, false),
            Builders<DeckConfiguration>.Filter.Lt(d => d.LastAnalyzedAt, cutoff));

        return await _decksCollection.Find(filter)
            .SortBy(d => d.LastAnalyzedAt)
            .Limit(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Builds admin analytics from a narrow Mongo projection so legacy or malformed deck
    /// documents cannot break the whole analytics payload.
    /// </summary>
    public async Task<DeckAnalyticsResult> GetAnalyticsAsync(DateTime last7, DateTime last30)
    {
        var totalDecks = (int)await _decksCollection.CountDocumentsAsync(_ => true);
        var decksLast7 = (int)await _decksCollection.CountDocumentsAsync(d => d.CreatedAt >= last7);
        var decksLast30 = (int)await _decksCollection.CountDocumentsAsync(d => d.CreatedAt >= last30);

        var analyticsDocs = await _decksCollection
            .Find(FilterDefinition<DeckConfiguration>.Empty)
            .Project<BsonDocument>(Builders<DeckConfiguration>.Projection
                .Include(d => d.Format)
                .Include(d => d.PowerLevel)
                .Include(d => d.BudgetRange)
                .Include(d => d.Colors)
                .Include(d => d.UserId)
                .Include(d => d.UserDisplayName))
            .ToListAsync();

        var byFormat = CountByStringField(analyticsDocs, nameof(DeckConfiguration.Format));
        var byPowerLevel = CountByStringField(analyticsDocs, nameof(DeckConfiguration.PowerLevel));
        var byBudget = CountByStringField(analyticsDocs, nameof(DeckConfiguration.BudgetRange));
        var byColor = CountByColors(analyticsDocs);
        var topUsers = analyticsDocs
            .Select(doc => new
            {
                UserId = GetStringField(doc, nameof(DeckConfiguration.UserId)),
                DisplayName = GetStringField(doc, nameof(DeckConfiguration.UserDisplayName))
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.UserId))
            .GroupBy(x => new
            {
                UserId = x.UserId!,
                DisplayName = string.IsNullOrWhiteSpace(x.DisplayName) ? x.UserId! : x.DisplayName!
            })
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new UserDeckCount
            {
                DisplayName = g.Key.DisplayName,
                Count = g.Count()
            })
            .ToList();

        return new DeckAnalyticsResult
        {
            TotalDecks = totalDecks,
            DecksLast7Days = decksLast7,
            DecksLast30Days = decksLast30,
            ByFormat = byFormat,
            ByColor = byColor,
            ByPowerLevel = byPowerLevel,
            ByBudget = byBudget,
            TopUsers = topUsers
        };
    }

    private static Dictionary<string, int> CountByStringField(IEnumerable<BsonDocument> documents, string fieldName)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            var value = GetStringField(document, fieldName) ?? "Unknown";
            counts[value] = counts.GetValueOrDefault(value) + 1;
        }

        return counts;
    }

    private static Dictionary<string, int> CountByColors(IEnumerable<BsonDocument> documents)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            var colors = GetStringArrayField(document, nameof(DeckConfiguration.Colors));
            if (colors.Count == 0)
            {
                counts["Colorless"] = counts.GetValueOrDefault("Colorless") + 1;
                continue;
            }

            foreach (var color in colors)
                counts[color] = counts.GetValueOrDefault(color) + 1;
        }

        return counts;
    }

    private static string? GetStringField(BsonDocument document, string fieldName)
    {
        if (!document.TryGetValue(fieldName, out var value) || value.IsBsonNull)
            return null;

        return value.BsonType switch
        {
            BsonType.String => value.AsString,
            _ => value.ToString()
        };
    }

    private static List<string> GetStringArrayField(BsonDocument document, string fieldName)
    {
        if (!document.TryGetValue(fieldName, out var value) || value.IsBsonNull)
            return new List<string>();

        return value.BsonType switch
        {
            BsonType.Array => value.AsBsonArray
                .Where(item => item.BsonType == BsonType.String && !string.IsNullOrWhiteSpace(item.AsString))
                .Select(item => item.AsString)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            BsonType.String when !string.IsNullOrWhiteSpace(value.AsString) => new List<string> { value.AsString },
            _ => new List<string>()
        };
    }
}
