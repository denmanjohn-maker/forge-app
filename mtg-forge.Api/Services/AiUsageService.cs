using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

/// <summary>
/// Records LLM API calls (user, operation, token counts) to the MongoDB <c>aiUsage</c>
/// collection and provides summary queries for the admin dashboard.
/// <para>
/// Every AI operation (generate, analyze, import description, etc.) logs an
/// <see cref="AiUsageRecord"/> so administrators can track costs and usage patterns
/// per user via <c>GET /api/admin/ai-usage</c>.
/// </para>
/// </summary>
public class AiUsageService
{
    private readonly IMongoCollection<AiUsageRecord> _collection;

    public AiUsageService(IOptions<MongoDbSettings> settings)
    {
        var client = new MongoClient(settings.Value.ConnectionString);
        var db = client.GetDatabase(settings.Value.DatabaseName);
        _collection = db.GetCollection<AiUsageRecord>("aiUsage");
        _collection.Indexes.CreateOne(
            new CreateIndexModel<AiUsageRecord>(
                Builders<AiUsageRecord>.IndexKeys.Descending(r => r.CreatedAt)));
    }

    /// <summary>Appends a single AI usage record to the collection (fire-and-forget friendly).</summary>
    public Task LogAsync(AiUsageRecord record) =>
        _collection.InsertOneAsync(record);

    /// <summary>
    /// Returns aggregated token usage from <paramref name="from"/> to now, broken down
    /// by user with per-operation counts.
    /// </summary>
    public async Task<AiUsageSummary> GetSummaryAsync(DateTime from)
    {
        var records = await _collection
            .Find(r => r.CreatedAt >= from)
            .ToListAsync();

        return new AiUsageSummary
        {
            TotalInputTokens = records.Sum(r => r.InputTokens),
            TotalOutputTokens = records.Sum(r => r.OutputTokens),
            ByUser = records
                .GroupBy(r => new { r.UserId, r.UserDisplayName })
                .Select(g => new UserUsageSummary
                {
                    DisplayName = g.Key.UserDisplayName,
                    GenerateCount = g.Count(r => r.Operation == "generate"),
                    AnalyzeCount = g.Count(r => r.Operation == "analyze"),
                    TotalInputTokens = g.Sum(r => r.InputTokens),
                    TotalOutputTokens = g.Sum(r => r.OutputTokens)
                })
                .OrderByDescending(u => u.TotalInputTokens + u.TotalOutputTokens)
                .ToList()
        };
    }
}
