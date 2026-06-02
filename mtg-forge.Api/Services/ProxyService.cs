using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MtgForge.Api.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MtgForge.Api.Services;

/// <summary>
/// Generates a printable proxy sheet PDF from a deck's card list, fetching card
/// images from Scryfall and laying them out in a 3×3 grid at standard card dimensions
/// (2.5" × 3.5" at 300 DPI) suitable for printing and sleeving.
/// </summary>
public class ProxyService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ProxyService> _logger;

    // Standard MTG card dimensions in points (1 inch = 72pt)
    private const float CardWidthPt  = 2.5f  * 72f; // 180pt
    private const float CardHeightPt = 3.5f  * 72f; // 252pt
    private const float GutterPt     = 4f;           // tighter gutter gives 8pt headroom on Letter
    private const int   CardsPerRow  = 3;
    private const int   RowsPerPage  = 3;
    private const int   CardsPerPage = CardsPerRow * RowsPerPage;

    public ProxyService(IHttpClientFactory httpFactory, ILogger<ProxyService> logger)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>
    /// Builds a proxy sheet PDF for the given deck.
    /// Cards are expanded by quantity (e.g. 4x Lightning Bolt → 4 copies on the sheet).
    /// Basic lands are excluded to save space; the commander appears once.
    /// </summary>
    public async Task<byte[]> GenerateProxySheetAsync(DeckConfiguration deck)
    {
        var expandedCards = deck.Cards
            .Where(c => !IsBasicLand(c))
            .SelectMany(c => Enumerable.Repeat(c, c.Quantity))
            .ToList();

        var imageMap = await FetchCardImagesAsync(expandedCards.Select(c => c.Name).Distinct().ToList());

        var pdf = Document.Create(container =>
        {
            var pages = (int)Math.Ceiling((double)expandedCards.Count / CardsPerPage);
            pages = Math.Max(pages, 1);

            for (var pageIdx = 0; pageIdx < pages; pageIdx++)
            {
                var pageCards = expandedCards.Skip(pageIdx * CardsPerPage).Take(CardsPerPage).ToList();
                var pageIdx2 = pageIdx; // capture for lambda

                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.MarginHorizontal(28); // 556pt available > 548pt needed (3×180 + 2×4)
                    page.MarginVertical(10);   // 772pt available > 764pt needed (3×252 + 2×4)

                    page.Content().Column(col =>
                    {
                        col.Spacing(GutterPt);

                        for (var row = 0; row < RowsPerPage; row++)
                        {
                            col.Item().Row(rowEl =>
                            {
                                rowEl.Spacing(GutterPt);

                                for (var col2 = 0; col2 < CardsPerRow; col2++)
                                {
                                    var cardIdx = row * CardsPerRow + col2;
                                    var card = cardIdx < pageCards.Count ? pageCards[cardIdx] : null;

                                    if (card != null && imageMap.TryGetValue(card.Name, out var imgBytes))
                                    {
                                        rowEl.ConstantItem(CardWidthPt).Height(CardHeightPt)
                                            .Image(imgBytes);
                                    }
                                    else if (card != null)
                                    {
                                        // Fallback: gray placeholder with card name
                                        rowEl.ConstantItem(CardWidthPt).Height(CardHeightPt)
                                            .Background("#e0e0e0")
                                            .AlignCenter().AlignMiddle()
                                            .Text(card.Name)
                                            .FontSize(8).WrapAnywhere();
                                    }
                                    else
                                    {
                                        // Empty slot on last page
                                        rowEl.ConstantItem(CardWidthPt).Height(CardHeightPt)
                                            .Background("#f5f5f5");
                                    }
                                }
                            });
                        }
                    });
                });
            }
        });

        return pdf.GeneratePdf();
    }

    private async Task<Dictionary<string, byte[]>> FetchCardImagesAsync(List<string> cardNames)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (cardNames.Count == 0) return result;

        var client = _httpFactory.CreateClient("Scryfall");

        // Step 1: Resolve image URIs via the batch collection endpoint (75 per request),
        // the same endpoint used by ScryfallService for enrichment.
        var imageUriMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        const int batchSize = 75;

        for (int i = 0; i < cardNames.Count; i += batchSize)
        {
            var batchOriginal = cardNames.Skip(i).Take(batchSize).ToList();
            var batchItems = batchOriginal
                .Select(n => (original: n, clean: CleanCardName(n)))
                .Where(x => !string.IsNullOrWhiteSpace(x.clean))
                .ToList();

            if (batchItems.Count == 0) continue;

            var identifiers = batchItems.Select(x => new { name = x.clean }).ToList();
            var body = JsonSerializer.Serialize(new { identifiers });

            try
            {
                var response = await client.PostAsync(
                    "https://api.scryfall.com/cards/collection",
                    new StringContent(body, Encoding.UTF8, "application/json"));

                if (!response.IsSuccessStatusCode)
                {
                    var errBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("ProxyService: Scryfall collection batch failed — HTTP {Status}. Body: {Body}. First name sent: {FirstName}",
                        (int)response.StatusCode, errBody, batchItems[0].clean);
                }
                else
                {
                    var collResult = await response.Content.ReadFromJsonAsync<ScryfallCollectionResult>();
                    if (collResult?.Data != null)
                    {
                        // Index canonical names → image URIs
                        var canonicalUris = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var card in collResult.Data)
                        {
                            if (string.IsNullOrEmpty(card.Name)) continue;
                            var uri = card.ImageUris?.Normal
                                ?? card.CardFaces?.FirstOrDefault()?.ImageUris?.Normal;
                            if (uri == null) continue;

                            canonicalUris[card.Name] = uri;
                            // DFC: "Front // Back" — also index by front face name
                            var slash = card.Name.IndexOf(" // ", StringComparison.Ordinal);
                            if (slash > 0) canonicalUris[card.Name[..slash]] = uri;
                        }

                        // Map original card names to URIs via cleaned name
                        foreach (var (original, clean) in batchItems)
                        {
                            if (canonicalUris.TryGetValue(clean, out var uri))
                                imageUriMap[original] = uri;
                        }

                        _logger.LogInformation("ProxyService: batch {Start} — {Found}/{Sent} cards resolved",
                            i, canonicalUris.Count, batchItems.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ProxyService: collection batch starting at {Start} threw", i);
            }

            if (i + batchSize < cardNames.Count)
                await Task.Delay(110);
        }

        _logger.LogInformation("ProxyService: resolved {UriCount}/{Total} image URIs", imageUriMap.Count, cardNames.Count);

        // Step 2: Download images sequentially to stay within Scryfall's rate limit
        foreach (var (name, uri) in imageUriMap)
        {
            try
            {
                var imgResp = await client.GetAsync(uri);
                if (imgResp.IsSuccessStatusCode)
                    result[name] = await imgResp.Content.ReadAsByteArrayAsync();
                else
                    _logger.LogWarning("ProxyService: image download failed for '{Card}' — HTTP {Status}", name, (int)imgResp.StatusCode);

                await Task.Delay(110);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ProxyService: exception downloading image for '{Card}'", name);
            }
        }

        _logger.LogInformation("ProxyService: fetched {Count}/{Total} card images", result.Count, cardNames.Count);
        return result;
    }

    /// <summary>
    /// Strips surrounding quotes, leading/trailing whitespace, and any control or
    /// zero-width Unicode characters that the LLM occasionally embeds in card names.
    /// </summary>
    private static string CleanCardName(string name) =>
        string.Concat(name.Trim().Trim('"').Where(c => !char.IsControl(c))).Trim();

    private static bool IsBasicLand(CardEntry card) =>
        card.CardType?.Contains("Basic Land", StringComparison.OrdinalIgnoreCase) == true ||
        new[] { "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes" }
            .Any(b => string.Equals(card.Name, b, StringComparison.OrdinalIgnoreCase));

    // Minimal Scryfall response DTOs
    private class ScryfallCollectionResult
    {
        [JsonPropertyName("data")]
        public List<ScryfallCard>? Data { get; set; }
    }

    private class ScryfallCard
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("image_uris")]
        public ScryfallImageUris? ImageUris { get; set; }
        [JsonPropertyName("card_faces")]
        public List<ScryfallCardFace>? CardFaces { get; set; }
    }

    private class ScryfallCardFace
    {
        [JsonPropertyName("image_uris")]
        public ScryfallImageUris? ImageUris { get; set; }
    }

    private class ScryfallImageUris
    {
        [JsonPropertyName("normal")]
        public string? Normal { get; set; }
    }
}
