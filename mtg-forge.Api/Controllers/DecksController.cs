using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MtgForge.Api.Models;
using MtgForge.Api.Services;

namespace MtgForge.Api.Controllers;

/// <summary>
/// Core REST controller for deck CRUD, AI generation, AI analysis, CSV import/export,
/// card management, combo detection, and collection ownership overlays.
/// <para>
/// All endpoints require authentication (<c>[Authorize]</c>). Ownership is enforced
/// per-deck: regular users can only access their own decks; the <c>Admin</c> role
/// bypasses ownership checks.
/// </para>
/// <para>
/// Deck generation is fire-and-forget:
/// <c>POST /api/decks/generate</c> returns HTTP 202 with a job ID immediately, and
/// the SPA polls <c>GET /api/decks/generate/status/{jobId}</c> until completion.
/// </para>
/// </summary>
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
    private readonly GenerationJobStore _jobStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SaltScoreService _saltService;
    private readonly CollectionService _collectionService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ProxyService _proxyService;

    public DecksController(DeckService deckService, IDeckGenerationService llmService, ScryfallService scryfallService, PricingService pricingService, ILogger<DecksController> logger, GenerationJobStore jobStore, IServiceScopeFactory scopeFactory, SaltScoreService saltService, CollectionService collectionService, IHttpClientFactory httpFactory, ProxyService proxyService)
    {
        _deckService = deckService;
        _llmService = llmService;
        _scryfallService = scryfallService;
        _pricingService = pricingService;
        _logger = logger;
        _jobStore = jobStore;
        _scopeFactory = scopeFactory;
        _saltService = saltService;
        _collectionService = collectionService;
        _httpFactory = httpFactory;
        _proxyService = proxyService;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private string GetDisplayName() => User.FindFirst("displayName")?.Value ?? User.Identity?.Name ?? "Unknown";
    private bool IsAdmin() => User.IsInRole("Admin");

    /// <summary>
    /// Returns a paginated, filtered list of decks. Non-admin users only see their
    /// own decks. Supports filtering by name (partial match), color, format, and
    /// power level.
    /// </summary>
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

    /// <summary>Returns a single deck by ID. Returns 403 if the deck belongs to another user.</summary>
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

    /// <summary>Searches decks by color and/or format, scoped to the current user unless they are an admin.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<List<DeckConfiguration>>> Search([FromQuery] string? color, [FromQuery] string? format)
    {
        var userId = IsAdmin() ? null : GetUserId();
        var decks = await _deckService.SearchAsync(color, format, userId);
        return Ok(decks);
    }

    /// <summary>
    /// Starts an async deck-generation job and returns HTTP 202 with the job ID.
    /// The background task calls the RAG pipeline, applies budget enforcement (up to
    /// 3 retries), and persists the deck to MongoDB on success.
    /// Poll <c>GET /api/decks/generate/status/{jobId}</c> for the result.
    /// </summary>
    [HttpPost("generate")]
    [EnableRateLimiting("deck-generation")]
    public IActionResult Generate([FromBody] DeckGenerationRequest request)
    {
        var userId = GetUserId();
        var displayName = GetDisplayName();
        var job = _jobStore.Create(userId);

        _ = Task.Run(async () =>
        {
            try
            {
                _jobStore.Update(job.Id, GenerationJobStatus.Running);
                using var scope = _scopeFactory.CreateScope();
                var llm = scope.ServiceProvider.GetRequiredService<IDeckGenerationService>();
                var pricing = scope.ServiceProvider.GetRequiredService<PricingService>();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<DecksController>>();
                var deckService = scope.ServiceProvider.GetRequiredService<DeckService>();

                logger.LogInformation("Generating deck (job {JobId}) with colors: {Colors}, format: {Format}",
                    job.Id, string.Join(",", request.Colors), request.Format);

                var deck = await llm.GenerateDeckAsync(request);
                await pricing.ApplyPricesAsync(deck.Cards);
                deck.TotalCards = deck.Cards.Sum(c => c.Quantity);
                deck.EstimatedTotalPrice = deck.Cards.Sum(c => c.EstimatedPrice * c.Quantity);

                var budgetMax = BudgetHelper.GetBudgetMax(request.BudgetRange);
                if (budgetMax.HasValue && deck.EstimatedTotalPrice > budgetMax.Value)
                {
                    var perCardMax = budgetMax.Value <= 50m ? 1.00m
                        : budgetMax.Value <= 150m ? 2.00m
                        : 5.00m;

                    var cheapCardPool = await pricing.GetCheapCardsAsync(perCardMax, 300);

                    const int maxRetries = 3;
                    for (var attempt = 0; attempt < maxRetries && deck.EstimatedTotalPrice > budgetMax.Value; attempt++)
                    {
                        var overage = deck.EstimatedTotalPrice - budgetMax.Value;
                        logger.LogInformation(
                            "Deck over budget by ${Overage:F2} (${Total:F2} vs ${Max:F2}). Attempt {Attempt} to fix.",
                            overage, deck.EstimatedTotalPrice, budgetMax.Value, attempt + 1);

                        var expensiveCards = deck.Cards
                            .Where(c => !c.Category.Equals("Commander", StringComparison.OrdinalIgnoreCase)
                                     && !c.CardType.Contains("Basic Land", StringComparison.OrdinalIgnoreCase)
                                     && c.EstimatedPrice > perCardMax)
                            .OrderByDescending(c => c.EstimatedPrice)
                            .Take(25)
                            .ToList();

                        if (expensiveCards.Count == 0) break;

                        var replacements = await llm.SuggestBudgetReplacementsAsync(
                            deck, expensiveCards, deck.EstimatedTotalPrice, budgetMax.Value, cheapCardPool);

                        if (replacements.Count == 0) break;

                        var existingNames = new HashSet<string>(
                            deck.Cards.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

                        for (var i = 0; i < Math.Min(expensiveCards.Count, replacements.Count); i++)
                        {
                            var replacement = replacements[i];
                            if (existingNames.Contains(replacement.Name)) continue;

                            var idx = deck.Cards.IndexOf(expensiveCards[i]);
                            if (idx >= 0)
                            {
                                existingNames.Remove(deck.Cards[idx].Name);
                                deck.Cards[idx] = replacement;
                                existingNames.Add(replacement.Name);
                            }
                        }

                        await pricing.ApplyPricesAsync(deck.Cards);
                        deck.TotalCards = deck.Cards.Sum(c => c.Quantity);
                        deck.EstimatedTotalPrice = deck.Cards.Sum(c => c.EstimatedPrice * c.Quantity);
                    }

                    if (deck.EstimatedTotalPrice > budgetMax.Value)
                        logger.LogWarning(
                            "Deck still over budget after replacements: ${Total:F2} vs ${Max:F2}",
                            deck.EstimatedTotalPrice, budgetMax.Value);
                }

                deck.UserId = userId;
                deck.UserDisplayName = displayName;
                var saved = await deckService.CreateAsync(deck);

                _jobStore.Update(job.Id, GenerationJobStatus.Completed, deck: saved);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deck generation failed for job {JobId} (user {UserId})", job.Id, userId);
                _jobStore.Update(job.Id, GenerationJobStatus.Failed, error: "Deck generation failed. Please try again.");
            }
        });

        return Accepted(new { jobId = job.Id });
    }

    /// <summary>Polls the status of an async deck-generation job. Returns 404 when the job has expired (>1 hour).</summary>
    [HttpGet("generate/status/{jobId}")]
    public IActionResult GetGenerationStatus(string jobId)
    {
        var job = _jobStore.Get(jobId);
        if (job is null)
            return NotFound(new { error = "Job not found or expired" });
        if (job.UserId != GetUserId() && !IsAdmin())
            return Forbid();

        return Ok(new
        {
            status = job.Status.ToString().ToLowerInvariant(),
            deck = job.Deck,
            error = job.Error
        });
    }

    /// <summary>Partially updates a deck. Only fields present in the request body are modified.</summary>
    [HttpPatch("{id}")]
    public async Task<ActionResult<DeckConfiguration>> Update(string id, [FromBody] DeckUpdateRequest request)
    {
        var deck = await _deckService.GetByIdAsync(id);
        if (deck is null)
            return NotFound();
        if (!IsAdmin() && deck.UserId != GetUserId())
            return Forbid();

        await _deckService.UpdateAsync(id, request, GetUserId());
        var updated = await _deckService.GetByIdAsync(id);
        return Ok(updated);
    }

    /// <summary>Creates a shallow copy of the deck, owned by the current user.</summary>
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

    /// <summary>
    /// Runs the AI deck-analysis pipeline and persists the result to the deck document.
    /// Analysis covers synergy, mana curve, category coverage, and card-upgrade suggestions.
    /// </summary>
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
            return StatusCode(500, new { error = "Failed to analyze deck" });
        }
    }

    /// <summary>
    /// Applies a single AI-suggested card swap: removes <c>RemoveCard</c>, enriches
    /// <c>AddCard</c> via Scryfall, applies local pricing, and saves the updated card list.
    /// </summary>
    [HttpPost("{id}/apply-upgrade")]
    public async Task<ActionResult<DeckConfiguration>> ApplyUpgrade(string id, [FromBody] CardUpgrade upgrade)
    {
        if (string.IsNullOrWhiteSpace(upgrade.RemoveCard) || string.IsNullOrWhiteSpace(upgrade.AddCard))
            return BadRequest(new { error = "RemoveCard and AddCard must be specified." });

        var deck = await _deckService.GetByIdAsync(id);
        if (deck is null)
            return NotFound();
        if (!IsAdmin() && deck.UserId != GetUserId())
            return Forbid();

        var cardToRemove = deck.Cards.FirstOrDefault(c =>
            string.Equals(c.Name, upgrade.RemoveCard, StringComparison.OrdinalIgnoreCase));
        if (cardToRemove is null)
            return BadRequest(new { error = $"Card '{upgrade.RemoveCard}' not found in deck." });

        var newCard = new CardEntry
        {
            Name = upgrade.AddCard,
            Quantity = cardToRemove.Quantity,
            Category = cardToRemove.Category,
            RoleInDeck = cardToRemove.RoleInDeck
        };

        var enriched = await _scryfallService.EnrichCardsAsync(new List<CardEntry> { newCard });
        if (enriched.Count > 0)
            newCard = enriched[0];
        await _pricingService.ApplyPricesAsync(new List<CardEntry> { newCard });

        deck.Cards.Remove(cardToRemove);
        deck.Cards.Add(newCard);
        deck.TotalCards = deck.Cards.Sum(c => c.Quantity);
        deck.EstimatedTotalPrice = deck.Cards.Sum(c => c.EstimatedPrice * c.Quantity);
        deck.UpdatedAt = DateTime.UtcNow;

        var updateRequest = new DeckUpdateRequest { Cards = deck.Cards };
        await _deckService.UpdateAsync(id, updateRequest, GetUserId());

        _logger.LogInformation("Applied upgrade on deck {Id}", id.Replace(Environment.NewLine, ""));

        return Ok(deck);
    }

    // === CSV Export (multiple formats) ===

    /// <summary>
    /// Exports the deck as a CSV file. The <paramref name="format"/> query parameter
    /// selects the target platform: <c>moxfield</c>, <c>archidekt</c>, <c>deckbox</c>,
    /// <c>deckstats</c>, or the default forge format.
    /// </summary>
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

    /// <summary>
    /// Imports a deck from a CSV file. The format (moxfield, archidekt, deckbox,
    /// deckstats, or default) is auto-detected from the header row. Cards are enriched
    /// via Scryfall, priced from the local cache, and a description is generated by the
    /// LLM before the deck is saved to MongoDB.
    /// </summary>
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
            return StatusCode(500, new { error = "Failed to import deck" });
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

    // === Deck History ===

    [HttpGet("{id}/history")]
    public async Task<ActionResult<List<DeckHistoryEntry>>> GetHistory(string id)
    {
        var deck = await _deckService.GetByIdAsync(id);
        if (deck is null) return NotFound();
        if (!IsAdmin() && deck.UserId != GetUserId()) return Forbid();

        var history = await _deckService.GetHistoryAsync(id);
        return Ok(history);
    }

    // === AI Card Recommendations ===

    [HttpGet("{id}/recommendations")]
    public async Task<ActionResult<List<CardRecommendation>>> GetRecommendations(string id)
    {
        try
        {
            var deck = await _deckService.GetByIdAsync(id);
            if (deck is null) return NotFound();
            if (!IsAdmin() && deck.UserId != GetUserId()) return Forbid();

            var recs = await _llmService.GetCardRecommendationsAsync(deck);
            var ownedNames = await _collectionService.GetOwnedNamesAsync(GetUserId());

            // Enrich with pricing data so the UI can show exact prices instead of just tier labels
            var recCards = recs.Select(r => new CardEntry { Name = r.Name, Quantity = 1 }).ToList();
            await _pricingService.ApplyPricesAsync(recCards);
            var priceMap = recCards.ToDictionary(c => c.Name, c => c.EstimatedPrice, StringComparer.OrdinalIgnoreCase);

            foreach (var rec in recs)
            {
                rec.IsOwned = ownedNames.Contains(rec.Name);
                if (priceMap.TryGetValue(rec.Name, out var price))
                    rec.EstimatedPrice = price;
            }
            return Ok(recs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recommendations for deck {Id}", id);
            return StatusCode(500, new { error = "Failed to generate recommendations" });
        }
    }

    // === Deck Metrics ===

    [HttpGet("{id}/metrics")]
    public async Task<IActionResult> GetMetrics(string id)
    {
        var deck = await _deckService.GetByIdAsync(id);
        if (deck is null) return NotFound();
        if (!IsAdmin() && deck.UserId != GetUserId()) return Forbid();

        var metrics = DeckMetricsCalculator.Calculate(deck.Cards);
        return Ok(new
        {
            metrics.ManaCurve,
            metrics.AverageCmc,
            metrics.LandCount,
            metrics.CreatureCount,
            metrics.RampCount,
            metrics.RemovalCount,
            metrics.CardDrawCount,
            metrics.ColorPipDistribution,
            metrics.TotalCost
        });
    }

    // === Budget Optimization ===

    [HttpPost("{id}/optimize-budget")]
    public async Task<IActionResult> OptimizeBudget(string id, [FromBody] OptimizeBudgetRequest request)
    {
        var deck = await _deckService.GetByIdAsync(id);
        if (deck is null) return NotFound();
        if (!IsAdmin() && deck.UserId != GetUserId()) return Forbid();

        var budgetMax = request.TargetBudget;
        if (budgetMax <= 0) return BadRequest(new { error = "TargetBudget must be greater than 0." });

        if (deck.EstimatedTotalPrice <= budgetMax)
            return Ok(deck);

        var perCardMax = budgetMax <= 50m ? 1.00m : budgetMax <= 150m ? 2.00m : 5.00m;
        var cheapCardPool = await _pricingService.GetCheapCardsAsync(perCardMax, 300);

        const int maxRetries = 3;
        for (var attempt = 0; attempt < maxRetries && deck.EstimatedTotalPrice > budgetMax; attempt++)
        {
            var expensiveCards = deck.Cards
                .Where(c => !c.Category.Equals("Commander", StringComparison.OrdinalIgnoreCase)
                         && !c.CardType.Contains("Basic Land", StringComparison.OrdinalIgnoreCase)
                         && c.EstimatedPrice > perCardMax)
                .OrderByDescending(c => c.EstimatedPrice)
                .Take(25)
                .ToList();

            if (expensiveCards.Count == 0) break;

            var replacements = await _llmService.SuggestBudgetReplacementsAsync(
                deck, expensiveCards, deck.EstimatedTotalPrice, budgetMax, cheapCardPool);

            if (replacements.Count == 0) break;

            var existingNames = new HashSet<string>(deck.Cards.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < Math.Min(expensiveCards.Count, replacements.Count); i++)
            {
                var replacement = replacements[i];
                if (existingNames.Contains(replacement.Name)) continue;
                var idx = deck.Cards.IndexOf(expensiveCards[i]);
                if (idx >= 0)
                {
                    existingNames.Remove(deck.Cards[idx].Name);
                    deck.Cards[idx] = replacement;
                    existingNames.Add(replacement.Name);
                }
            }

            await _pricingService.ApplyPricesAsync(deck.Cards);
            deck.TotalCards = deck.Cards.Sum(c => c.Quantity);
            deck.EstimatedTotalPrice = deck.Cards.Sum(c => c.EstimatedPrice * c.Quantity);
        }

        var updateRequest = new DeckUpdateRequest { Cards = deck.Cards };
        await _deckService.UpdateAsync(id, updateRequest, GetUserId());

        var updated = await _deckService.GetByIdAsync(id);
        return Ok(updated);
    }

    // === Deck Refinement ===

    [HttpPost("{id}/refine")]
    public IActionResult RefineDeck(string id, [FromBody] DeckRefinementRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefinementPrompt))
            return BadRequest(new { error = "RefinementPrompt must be specified." });

        var userId = GetUserId();
        var displayName = GetDisplayName();

        var job = _jobStore.Create(userId);

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var deckService = scope.ServiceProvider.GetRequiredService<DeckService>();
                var llm = scope.ServiceProvider.GetRequiredService<IDeckGenerationService>();
                var pricing = scope.ServiceProvider.GetRequiredService<PricingService>();

                var existingDeck = await deckService.GetByIdAsync(id);
                if (existingDeck is null)
                {
                    _jobStore.Update(job.Id, GenerationJobStatus.Failed, error: "Deck not found.");
                    return;
                }

                var refined = await llm.RefineDeckAsync(existingDeck, request.RefinementPrompt);
                await pricing.ApplyPricesAsync(refined.Cards);
                refined.TotalCards = refined.Cards.Sum(c => c.Quantity);
                refined.EstimatedTotalPrice = refined.Cards.Sum(c => c.EstimatedPrice * c.Quantity);
                refined.UserId = userId;
                refined.UserDisplayName = displayName;

                var saved = await deckService.CreateAsync(refined);
                _jobStore.Update(job.Id, GenerationJobStatus.Completed, deck: saved);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deck refinement failed for job {JobId}", job.Id);
                _jobStore.Update(job.Id, GenerationJobStatus.Failed, error: "Deck refinement failed. Please try again.");
            }
        });

        return Accepted(new { jobId = job.Id });
    }

    [HttpPost("{id}/add-card")]
    public async Task<ActionResult<DeckConfiguration>> AddCard(string id, [FromBody] AddCardRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CardName))
            return BadRequest(new { error = "CardName must be specified." });

        var deck = await _deckService.GetByIdAsync(id);
        if (deck is null)
            return NotFound();
        if (!IsAdmin() && deck.UserId != GetUserId())
            return Forbid();

        // Reject if the card is already in the deck (case-insensitive)
        if (deck.Cards.Any(c => string.Equals(c.Name, request.CardName, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new { error = $"'{request.CardName}' is already in this deck." });

        var newCard = new CardEntry
        {
            Name = request.CardName,
            Quantity = 1,
            Category = !string.IsNullOrWhiteSpace(request.Category) ? request.Category : "Mainboard",
            RoleInDeck = ""
        };

        var enriched = await _scryfallService.EnrichCardsAsync(new List<CardEntry> { newCard });
        if (enriched.Count > 0)
            newCard = enriched[0];
        await _pricingService.ApplyPricesAsync(new List<CardEntry> { newCard });

        deck.Cards.Add(newCard);
        deck.TotalCards = deck.Cards.Sum(c => c.Quantity);
        deck.EstimatedTotalPrice = deck.Cards.Sum(c => c.EstimatedPrice * c.Quantity);
        deck.UpdatedAt = DateTime.UtcNow;

        var updateRequest = new DeckUpdateRequest { Cards = deck.Cards };
        var updated = await _deckService.UpdateAsync(id, updateRequest, GetUserId());
        if (!updated)
        {
            _logger.LogWarning(
                "AddCard: update did not apply for deck {Id} after adding '{Card}'",
                id.Replace(Environment.NewLine, ""),
                request.CardName.Replace(Environment.NewLine, ""));
            return Conflict(new { error = "The deck could not be updated. Please refresh and try again." });
        }

        var persistedDeck = await _deckService.GetByIdAsync(id);

        _logger.LogInformation(
            "AddCard: added '{Card}' to deck {Id}",
            request.CardName.Replace(Environment.NewLine, ""), id.Replace(Environment.NewLine, ""));

        return Ok(persistedDeck ?? deck);
    }

    // === Salt Scores ===

    [HttpGet("{id}/salt")]
    public async Task<IActionResult> GetSaltScores(string id)
    {
        var deck = await _deckService.GetByIdAsync(id);
        if (deck is null) return NotFound();
        if (!IsAdmin() && deck.UserId != GetUserId()) return Forbid();

        var allScores = await _saltService.GetSaltScoresAsync();
        var cardScores = deck.Cards
            .Select(c => new
            {
                name = c.Name,
                saltScore = allScores.TryGetValue(c.Name, out var s) ? s : 0.0,
                quantity = c.Quantity
            })
            .ToList();

        var totalSalt = cardScores.Sum(c => c.saltScore * c.quantity);

        return Ok(new { totalSalt, cards = cardScores });
    }

    // === Combo Detection ===

    [HttpGet("{id}/combos")]
    public async Task<IActionResult> GetCombos(string id)
    {
        var deck = await _deckService.GetByIdAsync(id);
        if (deck is null) return NotFound();
        if (!IsAdmin() && deck.UserId != GetUserId()) return Forbid();

        try
        {
            var commanders = deck.Cards
                .Where(c => c.Category.Equals("Commander", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Name)
                .ToList();
            var mainCards = deck.Cards
                .Where(c => !c.Category.Equals("Commander", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Name)
                .ToList();

            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("mtg-forge/1.0");

            var payload = System.Text.Json.JsonSerializer.Serialize(new { commanders, main = mainCards });
            using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                "https://backend.commanderspellbook.com/find-my-combos/", content);

            if (!response.IsSuccessStatusCode)
                return Ok(new { results = new { included = Array.Empty<object>(), almostIncluded = Array.Empty<object>() } });

            var body = await response.Content.ReadAsStringAsync();
            return Content(body, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Combo detection failed for deck {Id}", id);
            return Ok(new { results = new { included = Array.Empty<object>(), almostIncluded = Array.Empty<object>() } });
        }
    }

    // === Collection Ownership Overlay ===

    [HttpGet("{id}/ownership")]
    public async Task<ActionResult<OwnershipResult>> GetOwnership(string id)
    {
        var deck = await _deckService.GetByIdAsync(id);
        if (deck is null) return NotFound();
        if (!IsAdmin() && deck.UserId != GetUserId()) return Forbid();

        var owned = await _collectionService.GetOwnedQuantitiesAsync(GetUserId());
        var totalCards = deck.Cards.Sum(c => c.Quantity);
        int ownedCount = 0;
        var missing = new List<MissingCard>();

        foreach (var card in deck.Cards)
        {
            owned.TryGetValue(card.Name, out var ownedQty);
            var need = Math.Max(0, card.Quantity - ownedQty);
            ownedCount += card.Quantity - need;
            if (need > 0)
                missing.Add(new MissingCard { Name = card.Name, Quantity = need, EstimatedPrice = card.EstimatedPrice });
        }

        var shoppingTotal = missing.Sum(m => m.Quantity * m.EstimatedPrice);
        var pct = totalCards > 0 ? Math.Round((decimal)ownedCount / totalCards * 100, 1) : 0;

        return Ok(new OwnershipResult
        {
            OwnedCount = ownedCount,
            TotalCards = totalCards,
            CompletionPct = pct,
            MissingCards = missing.OrderByDescending(m => m.EstimatedPrice).ToList(),
            ShoppingListTotal = shoppingTotal
        });
    }

    // === Proxy Sheet Generation ===

    [HttpGet("{id}/proxy")]
    public async Task<IActionResult> GetProxySheet(string id)
    {
        var deck = await _deckService.GetByIdAsync(id);
        if (deck is null) return NotFound();
        if (!IsAdmin() && deck.UserId != GetUserId()) return Forbid();

        try
        {
            var pdfBytes = await _proxyService.GenerateProxySheetAsync(deck);
            var safeName = string.Concat((deck.DeckName ?? "deck").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
            return File(pdfBytes, "application/pdf", $"{safeName}-proxies.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Proxy sheet generation failed for deck {Id}", id);
            return StatusCode(500, new { error = "Failed to generate proxy sheet." });
        }
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
