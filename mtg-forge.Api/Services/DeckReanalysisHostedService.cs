namespace MtgForge.Api.Services;

public class DeckReanalysisHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeckReanalysisHostedService> _logger;
    private const int MaxDecksPerRun = 20;
    private const int StaleAfterDays = 7;

    public DeckReanalysisHostedService(
        IServiceProvider serviceProvider,
        ILogger<DeckReanalysisHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunReanalysisAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeckReanalysisHostedService: scheduled run failed");
            }

            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        }
    }

    public async Task<int> RunReanalysisAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var deckService = scope.ServiceProvider.GetRequiredService<DeckService>();
        var generationService = scope.ServiceProvider.GetRequiredService<IDeckGenerationService>();

        var cutoff = DateTime.UtcNow.AddDays(-StaleAfterDays);
        var stale = await deckService.GetStaleDecksAsync(cutoff, MaxDecksPerRun);

        _logger.LogInformation(
            "DeckReanalysisHostedService: re-analyzing {Count} stale decks", stale.Count);

        int succeeded = 0;
        foreach (var deck in stale)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                var analysis = await generationService.AnalyzeDeckAsync(deck);
                await deckService.UpdateAnalysisAsync(deck.Id!, analysis);
                succeeded++;
                _logger.LogDebug(
                    "DeckReanalysisHostedService: re-analyzed '{Name}' → {Rating}",
                    deck.DeckName, analysis.OverallRating);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "DeckReanalysisHostedService: failed to analyze deck {Id}", deck.Id);
            }
        }

        _logger.LogInformation(
            "DeckReanalysisHostedService: completed {Succeeded}/{Total} re-analyses",
            succeeded, stale.Count);
        return succeeded;
    }
}
