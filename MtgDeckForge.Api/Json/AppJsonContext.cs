using System.Text.Json;
using System.Text.Json.Serialization;
using MtgDeckForge.Api.Models;
using MtgDeckForge.Api.Services;

namespace MtgDeckForge.Api.Json;

/// <summary>
/// Source-generated JSON serialization context shared across services.
/// Eliminates runtime reflection for JSON serialization, reduces memory usage,
/// and enables Native AOT compatibility.
/// </summary>
[JsonSerializable(typeof(DeckConfiguration))]
[JsonSerializable(typeof(DeckAnalysis))]
[JsonSerializable(typeof(CardEntry))]
[JsonSerializable(typeof(List<CardEntry>))]
[JsonSerializable(typeof(ClaudeResponse))]
[JsonSerializable(typeof(ScryfallCollectionResult))]
[JsonSerializable(typeof(ScryfallCard))]
[JsonSerializable(typeof(ScryfallPrices))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
public partial class AppJsonContext : JsonSerializerContext
{
}
