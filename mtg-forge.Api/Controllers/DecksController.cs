using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MtgForge.Api.Models;
using MtgForge.Api.Services;

namespace MtgForge.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DecksController : ControllerBase
{
    private readonly DeckService _deckService;
    private readonly IDeckGenerationService _llmService;
    private readonly ScryfallService _scryfallService;
    private readonly PricingService _pricingService;
    private readonly ILogger<DecksController> _logger;

    public DecksController(DeckService deckService, IDeckGenerationService llmService, ScryfallService scryfallService, PricingService pricingService, ILogger<DecksController> logger)
    {
        _deckService = deckService;
        _llmService = llmService;
        _scryfallService = scryfallService;
        _pricingService = pricingService;
        _logger = logger;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private string GetDisplayName() => User.FindFirst("displayName")?.Value ?? User.Identity?.Name ?? "Unknown";
    private bool IsAdmin() => User.IsInRole("Admin");

    [HttpGet]
    public async Task<ActionResult<PagedResult<DeckConfiguration>>> GetAll(
        [FromQuery] string? name,
        [FromQuery] string? color,
        [FromQuery] string? format,
        [FromQuery] string? powerLevel,
        [FromQuery] int skip = 0,
        [FromQuery] int limit = 12)
    {
        limit = Math.Clamp(limit, 1, 100);
        var result = await _deckService.GetPagedAsync(
            GetUserId(), IsAdmin(), name, color, format, powerLevel, skip, limit);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DeckConfiguration>> GetById(string id)
    {
        var deck = await _deckService.GetByIdAsync(id);
        if (deck is null)
            return NotFound();
        if (!IsAdmin() && deck.UserId != GetUserId())
            return Forbid();
        return Ok(deck);
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<DeckConfiguration>>> Search([FromQuery] string? color, [FromQuery] string? format)
    {
        var userId = IsAdmin() ? null : GetUserId();
        var decks = await _deckService.SearchAsync(color, format, userId);
        return Ok(decks);
    }

    [HttpPost("generate")]
    [EnableRateLimiting("deck-generation")]
    public async Task<ActionResult<DeckConfiguration>> Generate([FromBody] DeckGenerationRequest request)
    {
        try
        {
            _logger.LogInformation("Generating deck with colors: {Colors}, format: {Format}",
                string.Join(",", request.Colors), request.Format);

            var deck = await _llmService.GenerateDeckAsync(request);
            await _pricingService.ApplyPricesAsync(deck.Cards);
            deck.TotalCards = deck.Cards.Sum(c => c.Quantity);
            deck.EstimatedTotalPrice = deck.Cards.Sum(c => c.EstimatedPrice * c.Quantity);

            // Budget enforcement: swap expensive cards if real prices exceed budget
            var budgetMax = BudgetHelper.GetBudgetMax(request.BudgetRange);
            if (budgetMax.HasValue && deck.EstimatedTotalPrice > budgetMax.Value)
            {
                // Determine per-card price ceiling based on budget tier
                var perCardMax = budgetMax.Value <= 50m ? 1.00m
                    : budgetMax.Value <= 150m ? 2.00m
                    : 5.00m;

                var cheapCardPool = await _pricingService.GetCheapCardsAsync(perCardMax, 300);

                const int maxRetries = 3;
                for (var attempt = 0; attempt < maxRetries && deck.EstimatedTotalPrice > budgetMax.Value; attempt++)
                {
                    var overage = deck.EstimatedTotalPrice - budgetMax.Value;
                    _logger.LogInformation(
                        "Deck over budget by ${Overage:F2} (${Total:F2} vs ${Max:F2}). Attempt {Attempt} to fix.",
                        overage, deck.EstimatedTotalPrice, budgetMax.Value, attempt + 1);

                    // Replace ALL cards that exceed the per-card price ceiling
                    var expensiveCards = deck.Cards
                        .Where(c => !c.Category.Equals("Commander", StringComparison.OrdinalIgnoreCase)
                                 && !c.CardType.Contains("Basic Land", StringComparison.OrdinalIgnoreCase)
                                 && c.EstimatedPrice > perCardMax)
                        .OrderByDescending(c => c.EstimatedPrice)
                        .Take(25)
                        .ToList();

                    if (expensiveCards.Count == 0) break;

                    var replacements = await _llmService.SuggestBudgetReplacementsAsync(
                        deck, expensiveCards, deck.EstimatedTotalPrice, budgetMax.Value, cheapCardPool);

                    if (replacements.Count == 0) break;

                    // Swap cards: match by index (replacement[i] replaces expensiveCards[i])
                    var existingNames = new HashSet<string>(
                        deck.Cards.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

                    for (var i = 0; i < Math.Min(expensiveCards.Count, replacements.Count); i++)
                    {
                        var replacement = replacements[i];
                        // Skip if the replacement is already in the deck (Commander singleton rule)
                        if (existingNames.Contains(replacement.Name)) continue;

                        var idx = deck.Cards.IndexOf(expensiveCards[i]);
                        if (idx >= 0)
                        {
                            existingNames.Remove(deck.Cards[idx].Name);
                            deck.Cards[idx] = replacement;
                            existingNames.Add(replacement.Name);
                        }
                    }

                    // Re-apply real prices to the new cards
                    await _pricingService.ApplyPricesAsync(deck.Cards);
                    deck.TotalCards = deck.Cards.Sum(c => c.Quantity);
                    deck.EstimatedTotalPrice = deck.Cards.Sum(c => c.EstimatedPrice * c.Quantity);
                }

                if (deck.EstimatedTotalPrice > budgetMax.Value)
                    _logger.LogWarning(
                        "Deck still over budget after replacements: ${Total:F2} vs ${Max:F2}",
                        deck.EstimatedTotalPrice, budgetMax.Value);
            }

            deck.UserId = GetUserId();
            deck.UserDisplayName = GetDisplayName();
            var saved = await _deckService.CreateAsync(deck);

            return CreatedAtAction(nameof(GetById), new { id = saved.Id }, saved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate deck");
            return StatusCode(500, new { error = "Failed to generate deck", details = ex.Message });
        }
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult<DeckConfiguration>> Update(string id, [FromBody] DeckUpdateRequest request)
    {
        var deck = await _deckService.GetByIdAsync(id);
        if (deck is null)
            return NotFound();
        if (!IsAdmin() && deck.UserId != GetUserId())
            return Forbid();

        await _deckService.UpdateAsync(id, request);
        var updated = await _deckService.GetByIdAsync(id);
        return Ok(updated);
    }

    [HttpPost("{id}/copy")]
    public async Task<ActionResult<DeckConfiguration>> Copy(string id)
    {
        var source = await _deckService.GetByIdAsync(id);
        if (source is null)
            return NotFound();
        if (!IsAdmin() && source.UserId != GetUserId())
            return Forbid();

        var copy = new DeckConfiguration
        {
            DeckName = $"Copy of {source.DeckName}",
            Commander = source.Commander,
            Strategy = source.Strategy,
            Format = source.Format,
            Colors = new List<string>(source.Colors),
            PowerLevel = source.PowerLevel,
            BudgetRange = source.BudgetRange,
            EstimatedTotalPrice = source.EstimatedTotalPrice,
            TotalCards = source.TotalCards,
            DeckDescription = source.DeckDescription,
            Cards = source.Cards.Select(c => new CardEntry
            {
                Name = c.Name,
                Quantity = c.Quantity,
                ManaCost = c.ManaCost,
                Cmc = c.Cmc,
                CardType = c.CardType,
                Category = c.Category,
                RoleInDeck = c.RoleInDeck,
                EstimatedPrice = c.EstimatedPrice
            }).ToList(),
            UserId = GetUserId(),
            UserDisplayName = GetDisplayName()
        };

        var saved = await _deckService.CreateAsync(copy);
        return CreatedAtAction(nameof(GetById), new { id = saved.Id }, saved);
    }

    [HttpPost("{id}/analyze")]
    public async Task<ActionResult<DeckAnalysis>> Analyze(string id)
    {
        try
        {
            var deck = await _deckService.GetByIdAsync(id);
            if (deck is null)
                return NotFound();
            if (!IsAdmin() && deck.UserId != GetUserId())
                return Forbid();

            _logger.LogInformation("Analyzing deck {Id}: {Name}", id, deck.DeckName);
            var analysis = await _llmService.AnalyzeDeckAsync(deck);

            // Persist the analysis so it can be recalled without re-querying the LLM
            await _deckService.UpdateAnalysisAsync(id, analysis);

            return Ok(analysis);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze deck {Id}", id);
            return StatusCode(500, new { error = "Failed to analyze deck", details = ex.Message });
        }
    }

    // === CSV Export (multiple formats) ===

    [HttpGet("{id}/export/csv")]
    public async Task<IActionResult> ExportCsv(string id, [FromQuery] string format = "default")
    {
        var deck = await _deckService.GetByIdAsync(id);
        if (deck is null)
            return NotFound();
        if (!IsAdmin() && deck.UserId != GetUserId())
            return Forbid();

        var csv = new System.Text.StringBuilder();
        var safeName = (deck.DeckName ?? "deck").Replace(" ", "_");

        switch (format.ToLower())
        {
            case "moxfield":
                csv.AppendLine("Count,Tradelist Count,Name,Edition,Condition,Language,Foil,Tags");
                foreach (var card in deck.Cards)
                {
                    var tags = card.Category.Equals("Commander", StringComparison.OrdinalIgnoreCase) ? "Commander" : "";
                    csv.AppendLine($"{card.Quantity},,\"{Esc(card.Name)}\",,Near Mint,English,No,\"{tags}\"");
                }
                break;

            case "archidekt":
                csv.AppendLine("Quantity,Name,Categories");
                foreach (var card in deck.Cards)
                    csv.AppendLine($"{card.Quantity},\"{Esc(card.Name)}\",\"{Esc(card.Category)}\"");
                break;

            case "deckbox":
                csv.AppendLine("Count,Tradelist Count,Name,Edition,Card Number,Condition,Language,Foil,Signed,Artist Proof,Altered Art,Misprint,Promo,Textless,My Price");
                foreach (var card in deck.Cards)
                    csv.AppendLine($"{card.Quantity},,\"{Esc(card.Name)}\",,,,English,,,,,,,,{card.EstimatedPrice:F2}");
                break;

            case "deckstats":
                csv.AppendLine("amount,card_name,is_commander,is_sideboard,is_maybeboard,is_foil");
                foreach (var card in deck.Cards)
                {
                    var isCmd = card.Category.Equals("Commander", StringComparison.OrdinalIgnoreCase) ? "1" : "0";
                    var isSide = card.Category.Equals("Sideboard", StringComparison.OrdinalIgnoreCase) ? "1" : "0";
                    csv.AppendLine($"{card.Quantity},\"{Esc(card.Name)}\",{isCmd},{isSide},0,0");
                }
                break;

            default:
                csv.AppendLine("Count,Name,Category,Mana Cost,CMC,Type,Role,Estimated Price");
                foreach (var card in deck.Cards)
                    csv.AppendLine($"{card.Quantity},\"{Esc(card.Name)}\",\"{Esc(card.Category)}\",\"{Esc(card.ManaCost)}\",{card.Cmc},\"{Esc(card.CardType)}\",\"{Esc(card.RoleInDeck)}\",{card.EstimatedPrice:F2}");
                break;
        }

        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"{safeName}_{format}.csv");
    }

    // === CSV Import (auto-detects format, enriches via Scryfall, generates description) ===

    [HttpPost("import/csv")]
    public async Task<ActionResult<DeckConfiguration>> ImportCsv(IFormFile file, [FromForm] string? deckName)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file provided" });

        try
        {
            using var reader = new StreamReader(file.OpenReadStream());
            var content = await reader.ReadToEndAsync();
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
                return BadRequest(new { error = "Empty file" });

            var headerLine = lines[0].Trim();
            var headerFields = ParseCsvLine(headerLine).Select(f => f.Trim().Trim('"').ToLower()).ToList();
            var detectedFormat = DetectFormat(headerFields);

            var cards = new List<CardEntry>();
            foreach (var rawLine in lines.Skip(1))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var fields = ParseCsvLine(line);
                var card = detectedFormat switch
                {
                    "moxfield"  => ParseMoxfield(fields, headerFields),
                    "archidekt" => ParseArchidekt(fields, headerFields),
                    "deckbox"   => ParseDeckbox(fields, headerFields),
                    "deckstats" => ParseDeckstats(fields, headerFields),
                    _           => ParseDefault(fields, headerFields)
                };

                if (card is not null && !string.IsNullOrEmpty(card.Name))
                    cards.Add(card);
            }

            if (cards.Count == 0)
                return BadRequest(new { error = "No valid cards found in CSV" });

            // Enrich cards with Scryfall data (mana cost, CMC, type, price)
            cards = await _scryfallService.EnrichCardsAsync(cards);
            await _pricingService.ApplyPricesAsync(cards);

            // Derive color identity from enriched mana costs
            var colors = _scryfallService.DeriveColors(cards);

            var resolvedDeckName = deckName ?? Path.GetFileNameWithoutExtension(file.FileName);
            var commander = cards.FirstOrDefault(c => c.Category.Equals("Commander", StringComparison.OrdinalIgnoreCase))?.Name ?? "";

            // Generate a flavorful description via the LLM
            var description = await _llmService.GenerateImportDescriptionAsync(resolvedDeckName, cards);

            var totalPrice = cards.Sum(c => c.EstimatedPrice * c.Quantity);

            var deck = new DeckConfiguration
            {
                UserId = GetUserId(),
                UserDisplayName = GetDisplayName(),
                DeckName = resolvedDeckName,
                Commander = commander,
                Strategy = "Imported",
                Format = "Commander",
                Colors = colors,
                Cards = cards,
                TotalCards = cards.Sum(c => c.Quantity),
                EstimatedTotalPrice = totalPrice,
                DeckDescription = description
            };

            var saved = await _deckService.CreateAsync(deck);
            return CreatedAtAction(nameof(GetById), new { id = saved.Id }, saved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import CSV");
            return StatusCode(500, new { error = "Failed to import deck", details = ex.Message });
        }
    }

    // === Format detection ===

    private static string DetectFormat(List<string> headers)
    {
        if (headers.Contains("tradelist count") && headers.Contains("tags"))
            return "moxfield";
        if (headers.Contains("categories") && headers.Contains("quantity"))
            return "archidekt";
        if (headers.Contains("tradelist count") && headers.Contains("card number"))
            return "deckbox";
        if (headers.Contains("card_name") || headers.Contains("is_commander"))
            return "deckstats";
        return "default";
    }

    // === Format-specific parsers ===

    private static CardEntry? ParseMoxfield(List<string> fields, List<string> headers)
    {
        var idx = MakeIndex(headers);
        var name = GetField(fields, idx, "name");
        if (string.IsNullOrEmpty(name)) return null;

        var tags = GetField(fields, idx, "tags");
        var isCommander = tags.Contains("Commander", StringComparison.OrdinalIgnoreCase);

        return new CardEntry
        {
            Quantity = int.TryParse(GetField(fields, idx, "count"), out var q) ? q : 1,
            Name = name,
            Category = isCommander ? "Commander" : "Mainboard"
        };
    }

    private static CardEntry? ParseArchidekt(List<string> fields, List<string> headers)
    {
        var idx = MakeIndex(headers);
        var name = GetField(fields, idx, "name");
        if (string.IsNullOrEmpty(name)) return null;

        return new CardEntry
        {
            Quantity = int.TryParse(GetField(fields, idx, "quantity"), out var q) ? q : 1,
            Name = name,
            Category = GetField(fields, idx, "categories") is var cat && !string.IsNullOrEmpty(cat) ? cat : "Mainboard"
        };
    }

    private static CardEntry? ParseDeckbox(List<string> fields, List<string> headers)
    {
        var idx = MakeIndex(headers);
        var name = GetField(fields, idx, "name");
        if (string.IsNullOrEmpty(name)) return null;

        return new CardEntry
        {
            Quantity = int.TryParse(GetField(fields, idx, "count"), out var q) ? q : 1,
            Name = name,
            Category = "Mainboard",
            EstimatedPrice = decimal.TryParse(GetField(fields, idx, "my price"), out var p) ? p : 0m
        };
    }

    private static CardEntry? ParseDeckstats(List<string> fields, List<string> headers)
    {
        var idx = MakeIndex(headers);
        var name = GetField(fields, idx, "card_name");
        if (string.IsNullOrEmpty(name)) return null;

        var isCommander = GetField(fields, idx, "is_commander") == "1";
        var isSideboard = GetField(fields, idx, "is_sideboard") == "1";

        return new CardEntry
        {
            Quantity = int.TryParse(GetField(fields, idx, "amount"), out var q) ? q : 1,
            Name = name,
            Category = isCommander ? "Commander" : isSideboard ? "Sideboard" : "Mainboard"
        };
    }

    private static CardEntry? ParseDefault(List<string> fields, List<string> headers)
    {
        var idx = MakeIndex(headers);
        var name = GetField(fields, idx, "name");
        if (string.IsNullOrEmpty(name) && fields.Count >= 2)
            name = fields[1].Trim().Trim('"');
        if (string.IsNullOrEmpty(name)) return null;

        return new CardEntry
        {
            Quantity = int.TryParse(GetField(fields, idx, "count") is var c && !string.IsNullOrEmpty(c) ? c : fields.ElementAtOrDefault(0)?.Trim(), out var q) ? q : 1,
            Name = name,
            Category = GetField(fields, idx, "category") is var cat && !string.IsNullOrEmpty(cat) ? cat : "Mainboard",
            ManaCost = GetField(fields, idx, "mana cost"),
            Cmc = int.TryParse(GetField(fields, idx, "cmc"), out var cmc) ? cmc : 0,
            CardType = GetField(fields, idx, "type"),
            RoleInDeck = GetField(fields, idx, "role"),
            EstimatedPrice = decimal.TryParse(GetField(fields, idx, "estimated price"), out var p) ? p : 0m
        };
    }

    // === Helpers ===

    private static Dictionary<string, int> MakeIndex(List<string> headers)
    {
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < headers.Count; i++)
            idx[headers[i]] = i;
        return idx;
    }

    private static string GetField(List<string> fields, Dictionary<string, int> idx, string header)
    {
        if (idx.TryGetValue(header, out var i) && i < fields.Count)
            return fields[i].Trim().Trim('"');
        return "";
    }

    private static string Esc(string s) => (s ?? "").Replace("\"", "\"\"");

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return fields;
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deck = await _deckService.GetByIdAsync(id);
        if (deck is null)
            return NotFound();
        if (!IsAdmin() && deck.UserId != GetUserId())
            return Forbid();

        await _deckService.DeleteAsync(id);
        return NoContent();
    }
}
