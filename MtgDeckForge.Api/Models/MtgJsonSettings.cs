namespace MtgDeckForge.Api.Models;

public class MtgJsonSettings
{
    public string PricesUrl { get; set; } = "https://mtgjson.com/api/v5/AllPricesToday.json";
    public string PrintingsUrl { get; set; } = "https://mtgjson.com/api/v5/AllPrintings.json";
}
