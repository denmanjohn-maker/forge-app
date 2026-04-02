namespace MtgDeckForge.Api.Models;

public class MongoDbSettings
{
    public string ConnectionString { get; set; } = null!;
    public string DatabaseName { get; set; } = null!;
    public string DecksCollectionName { get; set; } = null!;
    public string UsersCollectionName { get; set; } = null!;
    public string GroupsCollectionName { get; set; } = null!;
}
