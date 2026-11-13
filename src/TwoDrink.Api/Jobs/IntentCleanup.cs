using Microsoft.EntityFrameworkCore;
using TwoDrink.Infrastructure.Data;
using TwoDrink.Infrastructure.Storage;

namespace TwoDrink.Api.Jobs;

/// <summary>
/// Removes check-in intents that were never confirmed: their upload blob (<c>checkins/{intentId}.jpg</c>) and the row.
/// An intent is only touched once it has been expired for more than <see cref="Grace"/>, so it can no longer be confirmed.
/// Best-effort per intent: a storage failure is logged and the row kept for the next run.
/// </summary>
public class IntentCleanup(AppDbContext db, IPhotoStorage storage, TimeProvider time, ILogger<IntentCleanup> logger)
{
    public static readonly TimeSpan Grace = TimeSpan.FromHours(1);
    public const int BatchSize = 500;

    /// <summary>Returns how many intents were removed.</summary>
    public async Task<int> CleanupAsync(CancellationToken ct)
    {
        var cutoff = time.GetUtcNow() - Grace;
        var stale = await db.CheckInIntents.AsNoTracking()
            .Where(i => i.ConfirmedCheckInId == null && i.ExpiresAt < cutoff)
            .OrderBy(i => i.ExpiresAt)
            .Select(i => new { i.Id, i.BlobName })
            .Take(BatchSize)
            .ToListAsync(ct);

        var removed = 0;
        foreach (var intent in stale)
        {
            try
            {
                await storage.DeleteAsync(intent.BlobName, ct);
                // Conditional: never removes an intent that got confirmed in the meantime.
                removed += await db.CheckInIntents
                    .Where(i => i.Id == intent.Id && i.ConfirmedCheckInId == null)
                    .ExecuteDeleteAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not clean up check-in intent {IntentId}", intent.Id);
            }
        }
        return removed;
    }
}

/// <summary>Runs <see cref="IntentCleanup.CleanupAsync"/> every hour (<c>CheckIns:IntentCleanupJobEnabled</c>).</summary>
public class IntentCleanupJob(IServiceScopeFactory scopes, ILogger<IntentCleanupJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
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
            var removed = await scope.ServiceProvider.GetRequiredService<IntentCleanup>().CleanupAsync(ct);
            if (removed > 0) logger.LogInformation("Removed {Count} unconfirmed check-in intent(s)", removed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Check-in intent cleanup failed");
        }
    }
}
