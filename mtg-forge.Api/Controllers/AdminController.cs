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

    // Candidate paths for the upstream status endpoint. mtg-forge-ai mounts
    // ingestion under /api/admin/ingest; older builds exposed /api/ingest.
    // We probe in order so the admin panel keeps working across both.
    private static readonly string[] StatusPathCandidates =
    {
        "/api/admin/ingest/status",
        "/api/admin/ingest",
        "/api/ingest/status",
        "/api/ingest"
    };

    private const string IngestTriggerPath = "/api/admin/ingest";
    private const string IngestTriggerFallbackPath = "/api/ingest";

    /// <summary>
    /// Proxies a GET to the mtg-forge-ai ingestion status endpoint.
    /// Returns a structured response describing the AI service's ingestion state.
    /// If the AI service is unreachable or misbehaves, returns a degraded status
    /// object rather than a 5xx, so the admin panel can show a friendly state
    /// with the underlying error message instead of a generic failure.
    /// </summary>
    [HttpGet("ai-status")]
    public async Task<IActionResult> GetAiStatus(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_ragSettings.BaseUrl))
        {
            return Ok(new { available = false, error = "RagPipeline:BaseUrl is not configured" });
        }

        HttpClient client;
        try
        {
            client = CreateMtgForgeAiClient(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AdminController: invalid RagPipeline:BaseUrl '{BaseUrl}'", _ragSettings.BaseUrl);
            return Ok(new { available = false, error = $"Invalid RagPipeline:BaseUrl: {ex.Message}" });
        }

        System.Net.HttpStatusCode? lastStatus = null;
        string? lastPath = null;

        foreach (var path in StatusPathCandidates)
        {
            try
            {
                using var response = await client.GetAsync(path, cancellationToken);
                lastStatus = response.StatusCode;
                lastPath = path;

                if (!response.IsSuccessStatusCode)
                {
                    // 404 means we should try the next candidate path. Anything
                    // else is a real upstream error — stop probing.
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        continue;
                    break;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                try
                {
                    using var json = System.Text.Json.JsonDocument.Parse(body);
                    return Ok(new { available = true, status = json.RootElement.Clone() });
                }
                catch (System.Text.Json.JsonException jex)
                {
                    _logger.LogWarning(
                        jex,
                        "AdminController: mtg-forge-ai returned non-JSON for {Path}: {Body}",
                        path,
                        Truncate(body, 500));
                    return Ok(new
                    {
                        available = false,
                        error = $"AI service returned a non-JSON response from {path}."
                    });
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                _logger.LogWarning(ex, "AdminController: could not reach mtg-forge-ai at {Path}", path);
                return Ok(new { available = false, error = $"AI service is unreachable: {ex.Message}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AdminController: unexpected error calling mtg-forge-ai at {Path}", path);
                return Ok(new { available = false, error = $"Unexpected error: {ex.Message}" });
            }
        }

        // All candidates exhausted — surface the last status code we saw (likely 404).
        if (lastStatus.HasValue)
        {
            _logger.LogWarning(
                "AdminController: mtg-forge-ai returned {Status} for {Path} (no candidate path matched)",
                lastStatus.Value,
                lastPath);

            return Ok(new
            {
                available = false,
                error = $"AI service returned HTTP {(int)lastStatus.Value} for {lastPath}"
            });
        }

        return Ok(new { available = false, error = "AI service did not respond to any known status path" });
    }

    /// <summary>
    /// Proxies a POST to the mtg-forge-ai ingest endpoint to trigger a manual
    /// card re-ingestion. Falls back to the legacy /api/ingest path if the
    /// canonical /api/admin/ingest returns 404.
    /// </summary>
    [HttpPost("ai-ingest")]
    public async Task<IActionResult> TriggerAiIngest(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_ragSettings.BaseUrl))
        {
            return StatusCode(500, new { error = "RagPipeline:BaseUrl is not configured" });
        }

        HttpClient client;
        try
        {
            client = CreateMtgForgeAiClient(TimeSpan.FromMinutes(10));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdminController: invalid RagPipeline:BaseUrl '{BaseUrl}'", _ragSettings.BaseUrl);
            return StatusCode(500, new { error = $"Invalid RagPipeline:BaseUrl: {ex.Message}" });
        }

        _logger.LogInformation("AdminController: triggering manual AI re-ingestion via mtg-forge-ai");

        try
        {
            using var response = await PostWithFallbackAsync(client, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "AdminController: mtg-forge-ai returned {Status} for ingest: {Body}",
                    response.StatusCode, err);
                return StatusCode(502, new { error = $"AI service returned HTTP {(int)response.StatusCode} from ingest endpoint." });
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(body);
                return Ok(new { success = true, result = json.RootElement.Clone() });
            }
            catch (System.Text.Json.JsonException)
            {
                return Ok(new { success = true, result = body });
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "AdminController: could not reach mtg-forge-ai to trigger ingestion");
            return StatusCode(503, new { error = $"AI service is unreachable: {ex.Message}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdminController: unexpected error triggering AI ingestion");
            return StatusCode(500, new { error = $"Unexpected error: {ex.Message}" });
        }
    }

    private static async Task<HttpResponseMessage> PostWithFallbackAsync(HttpClient client, CancellationToken ct)
    {
        // Try the canonical /api/admin/ingest first; fall back to legacy /api/ingest on 404.
        var response = await PostIngestAsync(client, IngestTriggerPath, ct);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            return response;

        response.Dispose();
        return await PostIngestAsync(client, IngestTriggerFallbackPath, ct);
    }

    private static Task<HttpResponseMessage> PostIngestAsync(HttpClient client, string path, CancellationToken ct)
    {
        // mtg-forge-ai's ingest endpoint requires a JSON body (even if empty).
        var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        return client.PostAsync(path, content, ct);
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "…";

    private HttpClient CreateMtgForgeAiClient(TimeSpan timeout)
    {
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_ragSettings.BaseUrl);
        client.Timeout = timeout;
        return client;
    }
}
