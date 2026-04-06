namespace MtgDeckForge.Api.Services;

public class PricingRefreshHostedService : IHostedLifecycleService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PricingRefreshHostedService> _logger;

    public PricingRefreshHostedService(IServiceProvider serviceProvider, ILogger<PricingRefreshHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        // Run initial import shortly after application has fully started
        await RunImportLoopAsync(cancellationToken);
    }

    private async Task RunImportLoopAsync(CancellationToken stoppingToken)
    {
        // Brief delay to let the app finish startup
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

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
