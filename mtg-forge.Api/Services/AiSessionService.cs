using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MtgForge.Api.Models;

namespace MtgForge.Api.Services;

public class AiSessionService
{
    private readonly IMongoCollection<AiChatSession> _sessions;

    public AiSessionService(IOptions<MongoDbSettings> settings)
    {
        var client = new MongoClient(settings.Value.ConnectionString);
        var database = client.GetDatabase(settings.Value.DatabaseName);

        _sessions = database.GetCollection<AiChatSession>(settings.Value.AiSessionsCollectionName);
        _sessions.Indexes.CreateOne(new CreateIndexModel<AiChatSession>(
            Builders<AiChatSession>.IndexKeys.Ascending(s => s.DeckId)));
    }

    public async Task<AiChatSession?> GetSessionAsync(string id) =>
        await _sessions.Find(s => s.Id == id).FirstOrDefaultAsync();

    public async Task<AiChatSession> CreateSessionAsync(string userId, string? deckId)
    {
        var session = new AiChatSession { UserId = userId, DeckId = deckId };
        await _sessions.InsertOneAsync(session);
        return session;
    }

    public async Task AddMessageAsync(string sessionId, AiChatMessage message)
    {
        var update = Builders<AiChatSession>.Update
            .Push(s => s.Messages, message)
            .Set(s => s.UpdatedAt, DateTime.UtcNow);

        await _sessions.UpdateOneAsync(s => s.Id == sessionId, update);
    }
}
