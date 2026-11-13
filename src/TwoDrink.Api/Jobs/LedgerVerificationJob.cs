using TwoDrink.Infrastructure.Ledger;

namespace TwoDrink.Api.Jobs;

/// <summary>Daily integrity check of every ledger chain. Broken chains are logged as Critical (an Azure alert hangs on it).</summary>
public class LedgerVerificationJob(IServiceScopeFactory scopes, ILogger<LedgerVerificationJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            do
            {
                await RunOnceAsync(stoppingToken);
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var broken = await scope.ServiceProvider.GetRequiredService<LedgerVerifier>().FindBrokenChainsAsync(ct);
            if (broken.Count > 0)
                logger.LogCritical("Ledger integrity check FAILED for {Count} account(s): {AccountIds}",
                    broken.Count, string.Join(", ", broken));
            else
                logger.LogInformation("Ledger integrity check passed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Ledger integrity check could not run");
        }
    }
}
