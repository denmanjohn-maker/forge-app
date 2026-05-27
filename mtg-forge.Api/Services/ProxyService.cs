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
    private const float GutterPt     = 6f;
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
                    page.MarginHorizontal(28); // 556pt available ≥ 552pt needed (3×180 + 2×6)
                    page.MarginVertical(10);   // 772pt available ≥ 768pt needed (3×252 + 2×6)

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
        var client = _httpFactory.CreateClient("Scryfall");

        // Scryfall rate limit: max 10 req/sec; stagger requests
        foreach (var name in cardNames)
        {
            try
            {
                var metaUrl = $"https://api.scryfall.com/cards/named?fuzzy={Uri.EscapeDataString(name)}";
                var metaResp = await client.GetAsync(metaUrl);
                if (!metaResp.IsSuccessStatusCode) continue;

                var meta = await metaResp.Content.ReadFromJsonAsync<ScryfallCard>();
                var imageUri = meta?.ImageUris?.Normal
                    ?? meta?.CardFaces?.FirstOrDefault()?.ImageUris?.Normal;

                if (imageUri is null) continue;

                var imgResp = await client.GetAsync(imageUri);
                if (imgResp.IsSuccessStatusCode)
                    result[name] = await imgResp.Content.ReadAsByteArrayAsync();

                await Task.Delay(110); // ~9 req/sec to stay under Scryfall's limit
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ProxyService: failed to fetch image for '{Card}'", name);
            }
        }

        return result;
    }

    private static bool IsBasicLand(CardEntry card) =>
        card.CardType?.Contains("Basic Land", StringComparison.OrdinalIgnoreCase) == true ||
        new[] { "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes" }
            .Any(b => string.Equals(card.Name, b, StringComparison.OrdinalIgnoreCase));

    // Minimal Scryfall response DTOs
    private class ScryfallCard
    {
        [System.Text.Json.Serialization.JsonPropertyName("image_uris")]
        public ScryfallImageUris? ImageUris { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("card_faces")]
        public List<ScryfallCardFace>? CardFaces { get; set; }
    }

    private class ScryfallCardFace
    {
        [System.Text.Json.Serialization.JsonPropertyName("image_uris")]
        public ScryfallImageUris? ImageUris { get; set; }
    }

    private class ScryfallImageUris
    {
        [System.Text.Json.Serialization.JsonPropertyName("normal")]
        public string? Normal { get; set; }
    }
}
