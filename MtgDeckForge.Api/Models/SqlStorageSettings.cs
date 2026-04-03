namespace MtgDeckForge.Api.Models;

public class SqlStorageSettings
{
    public string ConnectionString { get; set; } = "Server=(localdb)\\MSSQLLocalDB;Database=MtgDeckForgeLocal;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
}
