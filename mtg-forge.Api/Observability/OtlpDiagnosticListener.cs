using System.Diagnostics.Tracing;
using Serilog;
using Serilog.Events;

namespace MtgForge.Api.Observability;

/// <summary>
/// Forwards OpenTelemetry SDK and OTLP exporter internal events to Serilog so that
/// export failures (wrong port, network errors, auth failures, etc.) are never silent.
/// </summary>
/// <remarks>
/// The OTel .NET SDK emits internal diagnostics via named EventSources rather than
/// ILogger, so they are invisible to normal application logging. This listener bridges
/// that gap by subscribing to the two relevant EventSources and re-emitting anything
/// at Warning level or above through Serilog.
///
/// Common events surfaced here:
///   - Connection refused / DNS failure when reaching Tempo/Jaeger
///   - gRPC status errors (UNAVAILABLE, UNIMPLEMENTED, etc.)
///   - Export timeout / batch overflow
/// </remarks>
internal sealed class OtlpDiagnosticListener : EventListener
{
    // EventSource names published by the OpenTelemetry .NET SDK
    private const string ExporterSourceName = "OpenTelemetry-Exporter-OpenTelemetryProtocol";
    private const string SdkSourceName = "OpenTelemetry-Sdk";

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name is ExporterSourceName or SdkSourceName)
            EnableEvents(eventSource, EventLevel.Warning);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        var level = eventData.Level switch
        {
            EventLevel.Critical      => LogEventLevel.Fatal,
            EventLevel.Error         => LogEventLevel.Error,
            EventLevel.Warning       => LogEventLevel.Warning,
            EventLevel.Informational => LogEventLevel.Information,
            _                        => LogEventLevel.Debug,
        };

        var message = eventData.Message ?? eventData.EventName ?? "(no message)";

        // Format payload args into the message template when present
        if (eventData.Payload is { Count: > 0 })
        {
            try
            {
                message = string.Format(message, eventData.Payload.ToArray());
            }
            catch (Exception ex)
            {
                // Format failed; log raw payload so nothing is lost
                var raw = string.Join(", ", eventData.Payload.Select(p => p?.ToString() ?? "null"));
                message = $"{message} [{raw}] (format error: {ex.GetType().Name}: {ex.Message})";
            }
        }

        Log.ForContext("SourceContext", $"OTel.{eventData.EventSource.Name}")
           .Write(level, "OTel internal [{EventName}]: {Message}", eventData.EventName, message);
    }
}
