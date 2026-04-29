using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MtgForge.Api.Models;

namespace MtgForge.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RagPipelineSettings _ragSettings;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IHttpClientFactory httpClientFactory,
        IOptions<RagPipelineSettings> ragSettings,
        ILogger<AdminController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ragSettings = ragSettings.Value;
        _logger = logger;
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
