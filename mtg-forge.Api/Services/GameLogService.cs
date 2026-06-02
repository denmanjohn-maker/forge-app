using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

public class GameLogService
{
    private readonly IMongoCollection<GameLog> _gameLogs;
    private readonly IMongoCollection<DeckWinRateStats> _winRates;
    private readonly ILogger<GameLogService> _logger;

    public GameLogService(IOptions<MongoDbSettings> settings, ILogger<GameLogService> logger)
    {
        _logger = logger;
        var client = new MongoClient(settings.Value.ConnectionString);
        var database = client.GetDatabase(settings.Value.DatabaseName);

        _gameLogs = database.GetCollection<GameLog>(settings.Value.GameLogsCollectionName);
        _winRates = database.GetCollection<DeckWinRateStats>(settings.Value.WinRateCacheCollectionName);

        // Ensure indexes
        CreateIndexes();
    }

    private void CreateIndexes()
    {
        var indexOptions = new CreateIndexOptions { Background = true };
        _gameLogs.Indexes.CreateOne(new CreateIndexModel<GameLog>(
            Builders<GameLog>.IndexKeys.Ascending(g => g.UserId).Ascending(g => g.DeckId).Descending(g => g.Date),
            indexOptions));
    }

    public async Task<List<GameLog>> GetByDeckAsync(string deckId) =>
        await _gameLogs.Find(g => g.DeckId == deckId).SortByDescending(g => g.Date).ToListAsync();

    public async Task<GameLog> CreateAsync(GameLog log)
    {
        await _gameLogs.InsertOneAsync(log);
        await SafeUpdateWinRateCacheAsync(log.DeckId);
        return log;
    }

    public async Task DeleteAsync(string id, string deckId)
    {
        await _gameLogs.DeleteOneAsync(g => g.Id == id);
        await SafeUpdateWinRateCacheAsync(deckId);
    }

    public async Task<DeckWinRateStats?> GetWinRateAsync(string deckId) =>
        await _winRates.Find(w => w.DeckId == deckId).FirstOrDefaultAsync();

    private async Task SafeUpdateWinRateCacheAsync(string deckId)
    {
        try
        {
            await UpdateWinRateCacheAsync(deckId);
        }
        catch (Exception ex)
        {
            // The game log itself persisted successfully; a cache-recompute
            // failure must not fail the whole request. Surface it for diagnosis.
            _logger.LogError(ex, "Failed to update win-rate cache for deck {DeckId}", deckId);
        }
    }

    private async Task UpdateWinRateCacheAsync(string deckId)
    {
        var pipeline = new EmptyPipelineDefinition<GameLog>()
            .Match(g => g.DeckId == deckId)
            .Group(
                g => g.DeckId,
                group => new 
                {
                    DeckId = group.Key,
                    TotalGames = group.Count(),
                    Wins = group.Sum(g => g.Result == "win" ? 1 : 0),
                    Losses = group.Sum(g => g.Result == "loss" ? 1 : 0),
                    Draws = group.Sum(g => g.Result == "draw" ? 1 : 0)
                }
            );

        var aggResult = await _gameLogs.Aggregate(pipeline).FirstOrDefaultAsync();

        if (aggResult == null)
        {
            await _winRates.DeleteOneAsync(w => w.DeckId == deckId);
            return;
        }

        var winRate = aggResult.TotalGames > 0 
            ? (double)aggResult.Wins / aggResult.TotalGames 
            : 0;

        var stats = new DeckWinRateStats
        {
            DeckId = deckId,
            TotalGames = aggResult.TotalGames,
            Wins = aggResult.Wins,
            Losses = aggResult.Losses,
            Draws = aggResult.Draws,
            WinRate = winRate,
            UpdatedAt = DateTime.UtcNow
        };

        await _winRates.ReplaceOneAsync(
            w => w.DeckId == deckId, 
            stats, 
            new ReplaceOptions { IsUpsert = true });
    }
}
