using Serilog;
using Serilog.Sinks.Grafana.Loki;

namespace MtgForge.Api.Observability;

internal sealed class DiagnosticLokiHttpClient : ILokiHttpClient
{
    private readonly HttpClient _httpClient = new();

    public void SetCredentials(LokiCredentials? credentials) { }

    public void SetTenant(string? tenant) { }

    public async Task<HttpResponseMessage> PostAsync(string requestUri, Stream contentStream)
    {
        try
        {
            using var content = new StreamContent(contentStream);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            var response = await _httpClient.PostAsync(requestUri, content);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                Log.ForContext("SourceContext", "LokiDiag")
                   .Warning("Loki push ← {Status} — {Body}", (int)response.StatusCode, body);
            }
            return response;
        }
        catch (Exception ex)
        {
            Log.ForContext("SourceContext", "LokiDiag")
               .Error(ex, "Loki push failed to {Uri}", requestUri);
            throw;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
