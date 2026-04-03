using System.ComponentModel.DataAnnotations;

namespace MtgDeckForge.Api.Models;

public class CardPrice
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string CardName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string NormalizedCardName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string SourceUuid { get; set; } = string.Empty;

    public decimal PriceUsd { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class PricingImportRun
{
    public int Id { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public bool Success { get; set; }
    public int ImportedCount { get; set; }
    public string? Message { get; set; }
}
