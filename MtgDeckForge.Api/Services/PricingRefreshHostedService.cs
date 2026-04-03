using MtgDeckForge.Api.Services;

namespace MtgDeckForge.Api.Services;

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
