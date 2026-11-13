using Microsoft.EntityFrameworkCore;
using TwoDrink.Domain.Scoring;
using TwoDrink.Infrastructure.Data;

namespace TwoDrink.Api.Features.Venues;

public class VenueTierService(AppDbContext db)
{
    /// <summary>
    /// Tier in force at <paramref name="at"/> (sponsorship window is [StartsAt, EndsAt)); the highest wins if several overlap.
    /// A null art URL means the default art.
    /// </summary>
    public async Task<(Tier Tier, string? ArtUrl)> CurrentAsync(Guid venueId, DateTimeOffset at, CancellationToken ct)
    {
        var rows = await db.VenueSponsorships.AsNoTracking()
            .Where(s => s.VenueId == venueId && s.StartsAt <= at && at < s.EndsAt)
            .Select(s => new { s.Tier, s.BadgeArtUrl })
            .ToListAsync(ct);
        var best = rows.OrderByDescending(r => r.Tier).FirstOrDefault();
        return best is null ? (Tier.Default, null) : (best.Tier, best.BadgeArtUrl);
    }

    /// <summary>Current tier for several venues at once (one query). Venues without a sponsorship are absent.</summary>
    public async Task<IReadOnlyDictionary<Guid, Tier>> CurrentTiersAsync(IReadOnlyCollection<Guid> venueIds, DateTimeOffset at, CancellationToken ct)
    {
        var rows = await db.VenueSponsorships.AsNoTracking()
            .Where(s => venueIds.Contains(s.VenueId) && s.StartsAt <= at && at < s.EndsAt)
            .Select(s => new { s.VenueId, s.Tier })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.VenueId).ToDictionary(g => g.Key, g => g.Max(r => r.Tier));
    }
}
