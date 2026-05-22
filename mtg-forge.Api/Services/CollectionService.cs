using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

/// <summary>
/// Manages a user's physical card collection stored in MongoDB.
/// Each <see cref="CollectionEntry"/> tracks a specific card+foil+condition combination
/// with a quantity. The collection is used for ownership overlays on deck views
/// (showing which cards the user already owns).
/// </summary>
public class CollectionService
{
    private readonly IMongoCollection<CollectionEntry> _collection;

    public CollectionService(IOptions<MongoDbSettings> settings)
    {
        var mongoClient = new MongoClient(settings.Value.ConnectionString);
        var mongoDatabase = mongoClient.GetDatabase(settings.Value.DatabaseName);
        _collection = mongoDatabase.GetCollection<CollectionEntry>(settings.Value.CollectionCollectionName);
        EnsureIndexes();
    }

    private void EnsureIndexes()
    {
        _collection.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<CollectionEntry>(
                Builders<CollectionEntry>.IndexKeys
                    .Ascending(e => e.UserId)
                    .Ascending(e => e.CardName)),
            new CreateIndexModel<CollectionEntry>(
                Builders<CollectionEntry>.IndexKeys.Ascending(e => e.UserId))
        });
    }

    /// <summary>
    /// Returns a page of collection entries for <paramref name="userId"/>, optionally
    /// filtered by a case-insensitive card-name substring search.
    /// </summary>
    public async Task<PagedResult<CollectionEntry>> GetPagedAsync(string userId, string? search, int skip, int limit)
    {
        var builder = Builders<CollectionEntry>.Filter;
        var filter = builder.Eq(e => e.UserId, userId);

        if (!string.IsNullOrWhiteSpace(search))
            filter &= builder.Regex(e => e.CardName, new MongoDB.Bson.BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(search), "i"));

        var total = (int)await _collection.CountDocumentsAsync(filter);
        var items = await _collection.Find(filter)
            .SortBy(e => e.CardName)
            .Skip(skip)
            .Limit(limit)
            .ToListAsync();

        return new PagedResult<CollectionEntry>
        {
            Items = items,
            Total = total,
            Skip = skip,
            Limit = limit
        };
    }

    /// <summary>Returns a set of card names (case-insensitive) owned by <paramref name="userId"/>.</summary>
    public async Task<HashSet<string>> GetOwnedNamesAsync(string userId)
    {
        var entries = await _collection
            .Find(e => e.UserId == userId)
            .Project(e => e.CardName)
            .ToListAsync();
        return new HashSet<string>(entries, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns a map of card name → total quantity owned by <paramref name="userId"/>,
    /// summing across all foil/condition variants.
    /// </summary>
    public async Task<Dictionary<string, int>> GetOwnedQuantitiesAsync(string userId)
    {
        var entries = await _collection
            .Find(e => e.UserId == userId)
            .ToListAsync();
        return entries
            .GroupBy(e => e.CardName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Quantity), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds cards to the user's collection. If an identical card+foil+condition entry
    /// already exists, the quantity is incremented rather than creating a duplicate row.
    /// </summary>
    public async Task<CollectionEntry> AddAsync(string userId, CollectionAddRequest req)
    {
        var normalizedName = req.CardName.Trim();
        // Merge into existing entry for same card+foil+condition if present (case-insensitive name match)
        var existing = await _collection.Find(
            e => e.UserId == userId &&
                 e.CardName.ToLower() == normalizedName.ToLower() &&
                 e.Foil == req.Foil &&
                 e.Condition == req.Condition)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            await _collection.UpdateOneAsync(
                e => e.Id == existing.Id,
                Builders<CollectionEntry>.Update.Inc(e => e.Quantity, req.Quantity));
            existing.Quantity += req.Quantity;
            return existing;
        }

        var entry = new CollectionEntry
        {
            UserId = userId,
            CardName = normalizedName,
            SetCode = req.SetCode,
            Quantity = req.Quantity,
            Foil = req.Foil,
            Condition = req.Condition,
            AddedAt = DateTime.UtcNow
        };
        await _collection.InsertOneAsync(entry);
        return entry;
    }

    /// <summary>Updates a collection entry's quantity, condition, foil flag, or set code.</summary>
    public async Task<bool> UpdateAsync(string userId, string id, CollectionUpdateRequest req)
    {
        var updates = new List<UpdateDefinition<CollectionEntry>>();
        var builder = Builders<CollectionEntry>.Update;

        if (req.Quantity.HasValue) updates.Add(builder.Set(e => e.Quantity, Math.Max(1, req.Quantity.Value)));
        if (req.Condition != null) updates.Add(builder.Set(e => e.Condition, req.Condition));
        if (req.Foil.HasValue)     updates.Add(builder.Set(e => e.Foil, req.Foil.Value));
        if (req.SetCode != null)   updates.Add(builder.Set(e => e.SetCode, req.SetCode));

        if (updates.Count == 0) return true;

        var result = await _collection.UpdateOneAsync(
            e => e.Id == id && e.UserId == userId,
            builder.Combine(updates));
        return result.ModifiedCount > 0;
    }

    /// <summary>Permanently deletes a collection entry owned by <paramref name="userId"/>.</summary>
    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var result = await _collection.DeleteOneAsync(e => e.Id == id && e.UserId == userId);
        return result.DeletedCount > 0;
    }

    /// <summary>
    /// Parses a CSV file and bulk-inserts its cards into the user's collection,
    /// merging duplicates. Supports Name, Quantity/Count, Set/Edition, Foil, and
    /// Condition columns. Returns the number of rows successfully imported.
    /// </summary>
    public async Task<int> BulkImportFromCsvAsync(string userId, string csvContent)
    {
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return 0;

        var headers = lines[0].Trim().Split(',').Select(h => h.Trim().Trim('"').ToLower()).ToList();
        var nameIdx   = headers.IndexOf("name");
        var qtyIdx    = headers.IndexOf("quantity");
        if (qtyIdx < 0) qtyIdx = headers.IndexOf("count");
        var setIdx    = headers.IndexOf("set");
        if (setIdx < 0) setIdx = headers.IndexOf("edition");
        var foilIdx   = headers.IndexOf("foil");
        var condIdx   = headers.IndexOf("condition");

        if (nameIdx < 0) return 0;

        int imported = 0;
        foreach (var raw in lines.Skip(1))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var fields = SplitCsvLine(line);
            if (fields.Count <= nameIdx) continue;

            var cardName = fields[nameIdx].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(cardName)) continue;

            var qty  = qtyIdx >= 0 && int.TryParse(GetField(fields, qtyIdx), out var q) ? Math.Max(1, q) : 1;
            var set  = setIdx >= 0  ? GetField(fields, setIdx)  : null;
            var foil = foilIdx >= 0 && GetField(fields, foilIdx).Equals("yes", StringComparison.OrdinalIgnoreCase);
            var cond = condIdx >= 0 ? GetField(fields, condIdx) : "NM";

            await AddAsync(userId, new CollectionAddRequest
            {
                CardName  = cardName,
                SetCode   = set,
                Quantity  = qty,
                Foil      = foil,
                Condition = string.IsNullOrWhiteSpace(cond) ? "NM" : cond
            });
            imported++;
        }
        return imported;
    }

    /// <summary>
    /// Exports the user's entire collection as a CSV string with columns:
    /// Name, Quantity, Set, Foil, Condition.
    /// </summary>
    public async Task<string> ExportToCsvAsync(string userId)
    {
        var entries = await _collection.Find(e => e.UserId == userId)
            .SortBy(e => e.CardName)
            .ToListAsync();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Name,Quantity,Set,Foil,Condition");
        foreach (var e in entries)
            sb.AppendLine($"\"{Esc(e.CardName)}\",{e.Quantity},\"{Esc(e.SetCode ?? "")}\",{(e.Foil ? "Yes" : "No")},{e.Condition}");
        return sb.ToString();
    }

    private static string GetField(List<string> fields, int idx) =>
        idx < fields.Count ? fields[idx].Trim().Trim('"') : "";

    private static string Esc(string s) => s.Replace("\"", "\"\"");

    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; }
            else if (c == ',' && !inQuotes) { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields;
    }
}
