using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MtgForge.Api.Models;
using MtgForge.Api.Services;

namespace MtgForge.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RagPipelineSettings _ragSettings;
    private readonly ILogger<AdminController> _logger;
    private readonly DeckService _deckService;
    private readonly AiUsageService _aiUsageService;
    private readonly DeckReanalysisHostedService _reanalysisService;

    public AdminController(
        IHttpClientFactory httpClientFactory,
        IOptions<RagPipelineSettings> ragSettings,
        ILogger<AdminController> logger,
        DeckService deckService,
        AiUsageService aiUsageService,
        DeckReanalysisHostedService reanalysisService)
    {
        _httpClientFactory = httpClientFactory;
        _ragSettings = ragSettings.Value;
        _logger = logger;
        _deckService = deckService;
        _aiUsageService = aiUsageService;
        _reanalysisService = reanalysisService;
    }

    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics()
    {
        var allDecks = await _deckService.GetAllAsync();
        var now = DateTime.UtcNow;
        var last7 = now.AddDays(-7);
        var last30 = now.AddDays(-30);

        var byFormat = new Dictionary<string, int>();
        var byColor = new Dictionary<string, int>();
        var byPowerLevel = new Dictionary<string, int>();
        var byBudget = new Dictionary<string, int>();

        foreach (var deck in allDecks)
        {
            if (!string.IsNullOrEmpty(deck.Format))
                byFormat[deck.Format] = byFormat.GetValueOrDefault(deck.Format) + 1;

            if (!string.IsNullOrEmpty(deck.PowerLevel))
                byPowerLevel[deck.PowerLevel] = byPowerLevel.GetValueOrDefault(deck.PowerLevel) + 1;

            if (!string.IsNullOrEmpty(deck.BudgetRange))
                byBudget[deck.BudgetRange] = byBudget.GetValueOrDefault(deck.BudgetRange) + 1;

            foreach (var color in deck.Colors ?? [])
                byColor[color] = byColor.GetValueOrDefault(color) + 1;
        }

        var topUsers = allDecks
            .Where(d => !string.IsNullOrEmpty(d.UserId))
            .GroupBy(d => new { d.UserId, d.UserDisplayName })
            .Select(g => new UserDeckCount
            {
                DisplayName = g.Key.UserDisplayName ?? g.Key.UserId ?? "Unknown",
                Count = g.Count()
            })
            .OrderByDescending(u => u.Count)
            .Take(10)
            .ToList();

        var result = new DeckAnalyticsResult
        {
            TotalDecks = allDecks.Count,
            DecksLast7Days = allDecks.Count(d => d.CreatedAt >= last7),
            DecksLast30Days = allDecks.Count(d => d.CreatedAt >= last30),
            ByFormat = byFormat,
            ByColor = byColor,
            ByPowerLevel = byPowerLevel,
            ByBudget = byBudget,
            TopUsers = topUsers
        };

        return Ok(result);
    }

    [HttpGet("usage")]
    public async Task<IActionResult> GetUsage([FromQuery] int days = 30)
    {
        var from = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
        var summary = await _aiUsageService.GetSummaryAsync(from);
        return Ok(summary);
    }

    [HttpPost("reanalyze")]
    public async Task<IActionResult> TriggerReanalysis(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AdminController: manual deck re-analysis triggered");
        var count = await _reanalysisService.RunReanalysisAsync(cancellationToken);
        return Ok(new { reanalyzed = count });
    }

    /// <summary>
    /// Proxies a GET to the mtg-forge-ai /api/ingest/status endpoint.
    /// Returns a structured response describing the AI service's ingestion state.
    /// If the AI service is unreachable, returns a degraded status object rather
    /// than a 5xx, so the admin panel can show a friendly "Unavailable" state.
    /// </summary>
    [HttpGet("ai-status")]
    public async Task<IActionResult> GetAiStatus(CancellationToken cancellationToken)
    {
        try
        {
            var client = CreateMtgForgeAiClient(TimeSpan.FromSeconds(10));
            var response = await client.GetAsync("/api/ingest/status", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "AdminController: mtg-forge-ai returned {Status} for /api/ingest/status",
                    response.StatusCode);

                return Ok(new
                {
                    available = false,
                    error = $"AI service returned HTTP {(int)response.StatusCode}"
                });
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = System.Text.Json.JsonDocument.Parse(body);
            return Ok(new { available = true, status = json.RootElement });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "AdminController: could not reach mtg-forge-ai for ingestion status");
            return Ok(new { available = false, error = "AI service is unreachable" });
        }
    }

    /// <summary>
    /// Proxies a POST to the mtg-forge-ai /api/ingest endpoint to trigger a
    /// manual card re-ingestion. Mirrors the /api/pricing/refresh pattern.
    /// </summary>
    [HttpPost("ai-ingest")]
    public async Task<IActionResult> TriggerAiIngest(CancellationToken cancellationToken)
    {
        try
        {
            var client = CreateMtgForgeAiClient(TimeSpan.FromMinutes(10));

            _logger.LogInformation("AdminController: triggering manual AI re-ingestion via mtg-forge-ai");

            var response = await client.PostAsync("/api/ingest", null, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "AdminController: mtg-forge-ai returned {Status} for /api/ingest: {Body}",
                    response.StatusCode, err);
                return StatusCode(502, new { error = $"AI service returned HTTP {(int)response.StatusCode}: {err}" });
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(body);
                return Ok(new { success = true, result = json.RootElement });
            }
            catch
            {
                return Ok(new { success = true, result = body });
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "AdminController: could not reach mtg-forge-ai to trigger ingestion");
            return StatusCode(503, new { error = "AI service is unreachable" });
        }
    }

    private HttpClient CreateMtgForgeAiClient(TimeSpan timeout)
    {
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_ragSettings.BaseUrl);
        client.Timeout = timeout;
        return client;
    }
}
