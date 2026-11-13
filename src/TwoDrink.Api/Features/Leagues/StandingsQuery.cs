using Microsoft.EntityFrameworkCore;
using TwoDrink.Domain.Leagues;
using TwoDrink.Domain.Rules;
using TwoDrink.Infrastructure.Data;
using TwoDrink.Infrastructure.Data.Entities;

namespace TwoDrink.Api.Features.Leagues;

public record StandingItem(Guid UserId, string DisplayName, decimal Points, int DistinctVenues, int Rank);

/// <summary>
/// League standings computed from the ledger in one SQL query. Per member whose membership is current or was current at the league end (<c>LeftAt == null || LeftAt >= end</c>): entries of their account with
/// <c>CreatedAt</c> in [max(JoinedAt, start), end+1 day), points = sum of all types (reversals subtract; a Reversal is windowed by the <c>CreatedAt</c> of the entry it reverses, not its own), distinct venues = among CheckIn entries.
/// </summary>
public class StandingsQuery(AppDbContext db)
{
    /// <summary>Null when the league does not exist.</summary>
    public async Task<IReadOnlyList<StandingItem>?> ForLeagueAsync(Guid leagueId, CancellationToken ct)
    {
        var league = await db.Leagues.AsNoTracking().Where(l => l.Id == leagueId)
            .Select(l => new { l.StartDate, l.EndDate }).SingleOrDefaultAsync(ct);
        if (league is null) return null;
        var (startUtc, endUtc) = BusinessTime.Range(league.StartDate, league.EndDate);

        var rows = await (from m in db.LeagueMemberships.AsNoTracking()
                          join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                          where m.LeagueId == leagueId && (m.LeftAt == null || m.LeftAt >= endUtc)
                          let windowStart = m.JoinedAt > startUtc ? m.JoinedAt : startUtc
                          select new
                          {
                              u.Id,
                              u.DisplayName,
                              // A Reversal counts only if the entry it reverses counts: its window time is the original's CreatedAt.
                              Points = db.LedgerEntries
                                  .Where(e => e.AccountId == u.LedgerAccountId)
                                  .Select(e => new
                                  {
                                      e.Points,
                                      WindowAt = e.Type == LedgerEntryType.Reversal && e.ReversesEntryId != null
                                          ? db.LedgerEntries.Where(o => o.Id == e.ReversesEntryId).Select(o => (DateTimeOffset?)o.CreatedAt).FirstOrDefault() ?? e.CreatedAt
                                          : e.CreatedAt,
                                  })
                                  .Where(x => x.WindowAt >= windowStart && x.WindowAt < endUtc)
                                  .Sum(x => (decimal?)x.Points) ?? 0m,
                              DistinctVenues = db.LedgerEntries
                                  .Where(e => e.AccountId == u.LedgerAccountId && e.CreatedAt >= windowStart && e.CreatedAt < endUtc
                                              && e.Type == LedgerEntryType.CheckIn && e.VenueId != null)
                                  .Select(e => e.VenueId).Distinct().Count(),
                          }).ToListAsync(ct);

        // Stable, deterministic order for equal ranks: display name, then user id.
        var ordered = rows.OrderBy(r => r.DisplayName ?? "", StringComparer.Ordinal).ThenBy(r => r.Id).ToList();
        var names = ordered.ToDictionary(r => r.Id, r => r.DisplayName ?? "");
        var scores = ordered.Select(r => new MemberScore(r.Id, r.Points, r.DistinctVenues));
        return LeagueStandings.Rank(scores)
            .Select(r => new StandingItem(r.Score.UserId, names[r.Score.UserId], r.Score.Points, r.Score.DistinctVenues, r.Rank))
            .ToList();
    }

    /// <summary>The members' scores, for award decisions.</summary>
    public async Task<IReadOnlyList<MemberScore>> ScoresAsync(Guid leagueId, CancellationToken ct) =>
        ((await ForLeagueAsync(leagueId, ct)) ?? []).Select(i => new MemberScore(i.UserId, i.Points, i.DistinctVenues)).ToList();
}
