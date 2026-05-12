using System.Diagnostics;

namespace MtgForge.Api.Observability;

/// <summary>
/// Central ActivitySource for mtg-forge custom spans.
///
/// Attribute names follow the OpenTelemetry Semantic Conventions for Generative AI systems:
/// https://opentelemetry.io/docs/specs/semconv/gen-ai/
///
/// gen_ai.* attributes enable Grafana Tempo and other backends to automatically recognise
/// these spans as AI calls and surface them in GenAI-aware dashboards.
/// </summary>
public static class MtgForgeActivitySource
{
    public const string Name = "MtgForge.Api";

    public static readonly ActivitySource Instance = new(Name, "1.0.0");

    // ── OpenTelemetry GenAI semantic convention attribute names ──────────────
    public const string GenAiSystem          = "gen_ai.system";
    public const string GenAiOperationName   = "gen_ai.operation.name";
    public const string GenAiRequestModel    = "gen_ai.request.model";
    public const string GenAiRequestMaxTokens = "gen_ai.request.max_tokens";
    public const string GenAiRequestTemperature = "gen_ai.request.temperature";
    public const string GenAiUsageInputTokens  = "gen_ai.usage.input_tokens";
    public const string GenAiUsageOutputTokens = "gen_ai.usage.output_tokens";

    // ── mtg-forge domain attributes ───────────────────────────────────────────
    public const string MtgDeckFormat    = "mtg.deck.format";
    public const string MtgDeckBudget    = "mtg.deck.budget";
    public const string MtgDeckPowerLevel = "mtg.deck.power_level";
    public const string MtgDeckId        = "mtg.deck.id";
    public const string MtgOperationType = "mtg.operation.type";

    // ── Well-known gen_ai.system values used in this project ─────────────────
    public const string SystemDeepInfra   = "deep_infra";
    public const string SystemTogetherAi  = "together_ai"; // kept for backwards compat
    public const string SystemMtgForgeAi  = "mtg_forge_ai";
}
