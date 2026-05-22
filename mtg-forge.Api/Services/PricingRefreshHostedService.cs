namespace MtgForge.Api.Services;

/// <summary>
/// Background service that refreshes the local card-price cache from MTGJSON once per
/// day. A 1-minute startup delay ensures the database migrations have completed before
/// the first import attempt.
/// <para>
/// Pricing data is stored in PostgreSQL via <see cref="MtgJsonPricingImportService"/>
/// and consumed by <see cref="PricingService"/> to look up card prices.
/// </para>
/// </summary>
public class PricingRefreshHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PricingRefreshHostedService> _logger;

    public PricingRefreshHostedService(IServiceProvider serviceProvider, ILogger<PricingRefreshHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var importer = scope.ServiceProvider.GetRequiredService<MtgJsonPricingImportService>();
                var result = await importer.ImportDailyAsync(stoppingToken);
                if (result.Success)
                    _logger.LogInformation("Daily pricing import succeeded, imported {Count} cards", result.ImportedCount);
                else
                    _logger.LogWarning("Daily pricing import failed: {Message}", result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pricing refresh loop iteration failed");
            }

            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        }
    }
}
