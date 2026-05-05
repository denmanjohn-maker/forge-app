using System.Diagnostics;
using Serilog;
using Serilog.Sinks.Grafana.Loki;

namespace MtgForge.Api.Observability;

// Temporary: logs each Loki push so the feed can be verified. Remove once confirmed working.
internal sealed class DiagnosticLokiHttpClient : ILokiHttpClient
{
    private readonly DefaultLokiHttpClient _inner = new();

    public void SetAuthCredentials(LokiCredentials credentials) =>
        _inner.SetAuthCredentials(credentials);

    public async Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content)
    {
        var diag = Log.ForContext("SourceContext", "LokiDiag");
        var sw = Stopwatch.StartNew();
        diag.Information("Loki push → {Uri}", requestUri);
        try
        {
            var response = await _inner.PostAsync(requestUri, content);
            sw.Stop();
            if (response.IsSuccessStatusCode)
            {
                diag.Information("Loki push ← {Status} ({Elapsed}ms)", (int)response.StatusCode, sw.ElapsedMilliseconds);
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                diag.Warning("Loki push ← {Status} ({Elapsed}ms) — {Body}",
                    (int)response.StatusCode, sw.ElapsedMilliseconds, body);
            }
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            diag.Error(ex, "Loki push failed after {Elapsed}ms to {Uri}", sw.ElapsedMilliseconds, requestUri);
            throw;
        }
    }

    public void Dispose() => _inner.Dispose();
}
