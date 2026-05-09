using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

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

    public Task LogAsync(AiUsageRecord record) =>
        _collection.InsertOneAsync(record);

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
