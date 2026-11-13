using Microsoft.EntityFrameworkCore;
using TwoDrink.Domain.Rules;
using TwoDrink.Infrastructure.Data;
using TwoDrink.Infrastructure.Data.Entities;
using TwoDrink.Infrastructure.Places;

namespace TwoDrink.Api.Features.Venues;

public class VenueService(AppDbContext db, IPlacesClient places, TimeProvider time, RulesOptions rules)
{
    /// <summary>Column sizes of <c>venues.name</c> / <c>venues.address</c>; longer Google data is truncated, not rejected.</summary>
    public const int MaxNameLength = 255, MaxAddressLength = 500;

    static PlaceResult Fit(PlaceResult r) => r with
    {
        Name = r.Name.Length > MaxNameLength ? r.Name[..MaxNameLength] : r.Name,
        Address = r.Address is { Length: > MaxAddressLength } a ? a[..MaxAddressLength] : r.Address,
    };

    /// <summary>Refreshes the venue from Places when its cache is older than <see cref="RulesOptions.VenueRefreshAge"/>.</summary>
    public async Task<Venue> EnsureFreshAsync(Venue v, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (now - v.CachedAt <= rules.VenueRefreshAge) return v;

        PlaceResult? details;
        try
        {
            details = await places.DetailsAsync(v.PlaceId, ct);
        }
        catch (PlacesUnavailableException)
        {
            return v; // keep serving the cached data; try again next time
        }
        if (details is null) return v;
        details = Fit(details);

        await db.Venues.Where(x => x.Id == v.Id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Name, details.Name)
            .SetProperty(x => x.Address, details.Address)
            .SetProperty(x => x.Lat, details.Location.Lat)
            .SetProperty(x => x.Lng, details.Location.Lng)
            .SetProperty(x => x.CachedAt, now), ct);

        v.Name = details.Name;
        v.Address = details.Address;
        v.Lat = details.Location.Lat;
        v.Lng = details.Location.Lng;
        v.CachedAt = now;
        return v;
    }

    /// <summary>
    /// Inserts or updates venues by <c>place_id</c> with one atomic statement per place (safe under concurrency)
    /// and returns the stored rows in the order given.
    /// </summary>
    public async Task<IReadOnlyList<Venue>> UpsertAsync(IReadOnlyList<PlaceResult> results, CancellationToken ct)
    {
        if (results.Count == 0) return [];
        var distinct = results.GroupBy(r => r.PlaceId).Select(g => Fit(g.First())).ToList();
        var now = time.GetUtcNow();
        foreach (var r in distinct)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO venues (id, place_id, name, address, lat, lng, cached_at)
                VALUES ({Guid.NewGuid()}, {r.PlaceId}, {r.Name}, {r.Address}, {r.Location.Lat}, {r.Location.Lng}, {now})
                ON CONFLICT (place_id) DO UPDATE
                SET name = EXCLUDED.name, address = EXCLUDED.address, lat = EXCLUDED.lat, lng = EXCLUDED.lng, cached_at = EXCLUDED.cached_at
                """, ct);
        }

        var ids = distinct.Select(r => r.PlaceId).ToList();
        var byPlace = await db.Venues.AsNoTracking().Where(v => ids.Contains(v.PlaceId)).ToDictionaryAsync(v => v.PlaceId, ct);
        return distinct.Select(r => byPlace[r.PlaceId]).ToList();
    }
}
