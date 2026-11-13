using TwoDrink.Domain.Presence;
using TwoDrink.Infrastructure.Places;

namespace TwoDrink.Api.Tests.Infrastructure;

/// <summary>Counts calls and returns configurable results. Call <see cref="Reset"/> at the start of a test.</summary>
public sealed class FakePlacesClient : IPlacesClient
{
    private int _nearbyCalls;
    private int _detailsCalls;

    public int NearbyCalls => _nearbyCalls;
    public int DetailsCalls => _detailsCalls;

    public Func<GeoPoint, IReadOnlyList<PlaceResult>> NearbyResult { get; set; } = DefaultNearby;
    public Func<string, PlaceResult?> DetailsResult { get; set; } = _ => null;

    public static IReadOnlyList<PlaceResult> DefaultNearby(GeoPoint p) =>
        [new PlaceResult("place-default", "Bar Default", "Rua A, 1", new GeoPoint(p.Lat, p.Lng))];

    public void Reset()
    {
        Interlocked.Exchange(ref _nearbyCalls, 0);
        Interlocked.Exchange(ref _detailsCalls, 0);
        NearbyResult = DefaultNearby;
        DetailsResult = _ => null;
    }

    public Task<IReadOnlyList<PlaceResult>> NearbyAsync(GeoPoint p, CancellationToken ct)
    {
        Interlocked.Increment(ref _nearbyCalls);
        return Task.FromResult(NearbyResult(p));
    }

    public Task<PlaceResult?> DetailsAsync(string placeId, CancellationToken ct)
    {
        Interlocked.Increment(ref _detailsCalls);
        return Task.FromResult(DetailsResult(placeId));
    }
}
