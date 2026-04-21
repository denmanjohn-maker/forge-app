using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MtgForge.Api.Models;
using MtgForge.Api.Services;

namespace MtgForge.Api.Pages.Decks;

public class GenerateModel : PageModel
{
    private readonly IDeckGenerationService _llmService;
    private readonly DeckService _deckService;
    private readonly PricingService _pricingService;

    public GenerateModel(IDeckGenerationService llmService, DeckService deckService, PricingService pricingService)
    {
        _llmService = llmService;
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

            var deck = await _llmService.GenerateDeckAsync(request);
            await _pricingService.ApplyPricesAsync(deck.Cards);
            deck.TotalCards = deck.Cards.Sum(c => c.Quantity);
            deck.EstimatedTotalPrice = deck.Cards.Sum(c => c.EstimatedPrice * c.Quantity);
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
