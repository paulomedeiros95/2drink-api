using Microsoft.EntityFrameworkCore;
using TwoDrink.Api.Features.Leagues;
using TwoDrink.Domain.Leagues;
using TwoDrink.Domain.Rules;
using TwoDrink.Infrastructure.Data;
using TwoDrink.Infrastructure.Data.Entities;

namespace TwoDrink.Api.Jobs;

/// <summary>Awards the Golden Trophy of leagues that have ended. Idempotent and safe across concurrent instances.</summary>
public class TrophyAwarder(AppDbContext db, StandingsQuery standings, TimeProvider time)
{
    /// <summary>Returns how many leagues were closed out by this call.</summary>
    public async Task<int> AwardDueAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var candidates = await db.Leagues.AsNoTracking().Where(l => l.TrophiesAwardedAt == null)
            .Select(l => new { l.Id, l.StartDate, l.EndDate }).ToListAsync(ct);
        var due = candidates.Where(l => BusinessTime.Range(l.StartDate, l.EndDate).EndUtcExclusive <= now).Select(l => l.Id).ToList();

        var awarded = 0;
        foreach (var id in due)
            if (await AwardLeagueAsync(id, now, ct)) awarded++;
        return awarded;
    }

    async Task<bool> AwardLeagueAsync(Guid leagueId, DateTimeOffset now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Row lock: a concurrent awarder blocks here, then sees TrophiesAwardedAt set and skips.
        var league = await db.Leagues.FromSql($"SELECT * FROM leagues WHERE id = {leagueId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (league is null || league.TrophiesAwardedAt != null) return false;

        var champions = LeagueStandings.Champions(await standings.ScoresAsync(leagueId, ct));
        foreach (var userId in champions)
            db.Trophies.Add(new Trophy { Id = Guid.NewGuid(), LeagueId = leagueId, UserId = userId, AwardedAt = now });
        await db.SaveChangesAsync(ct);
        await db.Leagues.Where(l => l.Id == leagueId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.TrophiesAwardedAt, now), ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return true;
    }
}

/// <summary>Runs <see cref="TrophyAwarder.AwardDueAsync"/> every 5 minutes.</summary>
public class TrophyAwarderJob(IServiceScopeFactory scopes, ILogger<TrophyAwarderJob> logger) : BackgroundService
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
            var n = await scope.ServiceProvider.GetRequiredService<TrophyAwarder>().AwardDueAsync(ct);
            if (n > 0) logger.LogInformation("Awarded trophies for {Count} league(s)", n);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Trophy awarding failed");
        }
    }
}
