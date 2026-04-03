using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MtgDeckForge.Api.Models;
using MtgDeckForge.Api.Services;

namespace MtgDeckForge.Api.Pages.Decks;

public class GenerateModel : PageModel
{
    private readonly ClaudeService _claudeService;
    private readonly DeckService _deckService;
    private readonly PricingService _pricingService;

    public GenerateModel(ClaudeService claudeService, DeckService deckService, PricingService pricingService)
    {
        _claudeService = claudeService;
        _deckService = deckService;
        _pricingService = pricingService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }
    public string? CreatedDeckId { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        try
        {
            var request = new DeckGenerationRequest
            {
                Colors = (Input.ColorsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                Format = Input.Format,
                PowerLevel = Input.PowerLevel,
                BudgetRange = Input.BudgetRange,
                PreferredStrategy = Input.PreferredStrategy,
                PreferredCommander = Input.PreferredCommander,
                AdditionalNotes = Input.AdditionalNotes
            };

            var deck = await _claudeService.GenerateDeckAsync(request);
            await _pricingService.ApplyPricesAsync(deck.Cards);
            deck.TotalCards = deck.Cards.Sum(c => c.Quantity);
            deck.EstimatedTotalPrice = deck.Cards.Sum(c => c.EstimatedPrice * c.Quantity);
            deck.UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            deck.UserDisplayName = User.FindFirst("displayName")?.Value ?? User.Identity?.Name ?? "Unknown";
            var created = await _deckService.CreateAsync(deck);
            CreatedDeckId = created.Id;
            return Page();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    public class InputModel
    {
        [Required]
        public string ColorsCsv { get; set; } = "W,U,B";

        [Required]
        public string Format { get; set; } = "Commander";

        [Required]
        public string PowerLevel { get; set; } = "Casual";

        [Required]
        public string BudgetRange { get; set; } = "Budget";

        public string? PreferredStrategy { get; set; }
        public string? PreferredCommander { get; set; }
        public string? AdditionalNotes { get; set; }
    }
}
