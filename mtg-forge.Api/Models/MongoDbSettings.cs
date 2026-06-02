namespace MtgForge.Api.Models;

public class MongoDbSettings
{
    public string ConnectionString { get; set; } = null!;
    public string DatabaseName { get; set; } = null!;
    public string DecksCollectionName { get; set; } = null!;
    public string UsersCollectionName { get; set; } = null!;
    public string GroupsCollectionName { get; set; } = null!;
    public string JobsCollectionName { get; set; } = "generationJobs";
    public string DeckHistoryCollectionName { get; set; } = "deckHistory";
    public string CollectionCollectionName { get; set; } = "userCollections";
    public string GameLogsCollectionName { get; set; } = "gameLogs";
    public string WinRateCacheCollectionName { get; set; } = "winRateCache";
    public string AiSessionsCollectionName { get; set; } = "aiSessions";
}
