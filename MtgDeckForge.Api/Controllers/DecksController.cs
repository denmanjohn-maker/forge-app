using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MtgDeckForge.Api.Models;
using MtgDeckForge.Api.Services;

namespace MtgDeckForge.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DecksController : ControllerBase
{
    private readonly DeckService _deckService;
    private readonly ClaudeService _claudeService;
    private readonly ILogger<DecksController> _logger;

    public DecksController(DeckService deckService, ClaudeService claudeService, ILogger<DecksController> logger)
    {
        _deckService = deckService;
        _claudeService = claudeService;
        _logger = logger;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private string GetDisplayName() => User.FindFirst("displayName")?.Value ?? User.Identity?.Name ?? "Unknown";
    private bool IsAdmin() => User.IsInRole("Admin");

    [HttpGet]
    public async Task<ActionResult<List<DeckConfiguration>>> GetAll()
    {
        var decks = IsAdmin()
            ? await _deckService.GetAllAsync()
            : await _deckService.GetByUserIdAsync(GetUserId());
        return Ok(decks);
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
    public async Task<ActionResult<DeckConfiguration>> Generate([FromBody] DeckGenerationRequest request)
    {
        try
        {
            _logger.LogInformation("Generating deck with colors: {Colors}, format: {Format}", 
                string.Join(",", request.Colors), request.Format);

            var deck = await _claudeService.GenerateDeckAsync(request);
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
            var analysis = await _claudeService.AnalyzeDeckAsync(deck);
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

    // === CSV Import (auto-detects format) ===

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
                    "moxfield" => ParseMoxfield(fields, headerFields),
                    "archidekt" => ParseArchidekt(fields, headerFields),
                    "deckbox" => ParseDeckbox(fields, headerFields),
                    "deckstats" => ParseDeckstats(fields, headerFields),
                    _ => ParseDefault(fields, headerFields)
                };

                if (card is not null && !string.IsNullOrEmpty(card.Name))
                    cards.Add(card);
            }

            if (cards.Count == 0)
                return BadRequest(new { error = "No valid cards found in CSV" });

            var deck = new DeckConfiguration
            {
                UserId = GetUserId(),
                UserDisplayName = GetDisplayName(),
                DeckName = deckName ?? Path.GetFileNameWithoutExtension(file.FileName),
                Commander = cards.FirstOrDefault(c => c.Category.Equals("Commander", StringComparison.OrdinalIgnoreCase))?.Name ?? "",
                Strategy = "Imported",
                Format = "Commander",
                Cards = cards,
                TotalCards = cards.Sum(c => c.Quantity),
                EstimatedTotalPrice = cards.Sum(c => c.EstimatedPrice * c.Quantity),
                DeckDescription = $"Imported from {file.FileName} ({detectedFormat} format)"
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
