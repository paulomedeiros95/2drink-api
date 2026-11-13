using Microsoft.EntityFrameworkCore;
using TwoDrink.Infrastructure.Data;

namespace TwoDrink.Api.Jobs;

/// <summary>Closes check-ins to complements. Only sets <c>ClosedAt</c>; never touches points.</summary>
public class CheckInCloser(AppDbContext db, TimeProvider time)
{
    /// <summary>Idempotent: an already-closed check-in keeps its original <c>ClosedAt</c>.</summary>
    public async Task CloseAsync(Guid checkInId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await db.CheckIns.Where(c => c.Id == checkInId && c.ClosedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ClosedAt, c => c.ComplementsUntil < now ? c.ComplementsUntil : now), ct);
    }

    /// <summary>Closes every check-in whose complement window is over; returns how many were closed.</summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return await db.CheckIns.Where(c => c.ClosedAt == null && c.ComplementsUntil <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ClosedAt, c => c.ComplementsUntil < now ? c.ComplementsUntil : now), ct);
    }
}

/// <summary>Runs <see cref="CheckInCloser.SweepAsync"/> every 5 minutes (a safety net: the rules read the clock themselves).</summary>
public class CheckInCloserJob(IServiceScopeFactory scopes, ILogger<CheckInCloserJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

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
            var closed = await scope.ServiceProvider.GetRequiredService<CheckInCloser>().SweepAsync(ct);
            if (closed > 0) logger.LogInformation("Closed {Count} expired check-in(s)", closed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Check-in sweep failed");
        }
    }
}
