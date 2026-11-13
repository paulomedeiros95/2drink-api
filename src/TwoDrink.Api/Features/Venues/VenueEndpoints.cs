using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using TwoDrink.Api.Features.Auth;
using TwoDrink.Api.Startup;
using TwoDrink.Api.Features.Seasons;
using TwoDrink.Domain.Errors;
using TwoDrink.Domain.Presence;
using TwoDrink.Domain.Rules;
using TwoDrink.Domain.Scoring;
using TwoDrink.Infrastructure.Places;

namespace TwoDrink.Api.Features.Venues;

public record NearbyVenue(Guid VenueId, string Name, string? Address, double Lat, double Lng, string Tier);

public static class VenueEndpoints
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

    /// <summary>The cached part of a nearby answer; the tier is always computed fresh.</summary>
    private sealed record CachedVenue(Guid Id, string Name, string? Address, double Lat, double Lng);

    public static IServiceCollection AddVenues(this IServiceCollection services, IConfiguration config)
    {
        services.AddMemoryCache();
        services.AddHttpClient<IPlacesClient, GooglePlacesClient>(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<VenueService>();
        services.AddScoped<VenueTierService>();
        services.AddScoped<SeasonService>();
        return services;
    }

    public static IEndpointRouteBuilder MapVenueEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/venues/nearby", async (string? lat, string? lng, IPlacesClient places, VenueService venues,
            VenueTierService tiers, IMemoryCache cache, TimeProvider time, ILoggerFactory logs, CancellationToken ct) =>
        {
            var point = ParseLocation(lat, lng);
            // ~110 m grid: requests in the same cell share one Google call for an hour.
            var key = string.Create(CultureInfo.InvariantCulture,
                $"nearby:{Math.Round(point.Lat, 3, MidpointRounding.AwayFromZero):F3}:{Math.Round(point.Lng, 3, MidpointRounding.AwayFromZero):F3}");

            if (!cache.TryGetValue(key, out IReadOnlyList<CachedVenue>? list) || list is null)
            {
                IReadOnlyList<PlaceResult> found;
                try
                {
                    found = await places.NearbyAsync(point, ct);
                }
                catch (PlacesUnavailableException e)
                {
                    logs.CreateLogger("Venues").LogWarning(e, "Google Places unavailable");
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Places unavailable",
                        extensions: new Dictionary<string, object?> { ["code"] = "places_unavailable" });
                }
                var stored = await venues.UpsertAsync(found, ct);
                list = stored.Select(v => new CachedVenue(v.Id, v.Name, v.Address, v.Lat, v.Lng)).ToList();
                cache.Set(key, list, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheLifetime });
            }

            var tierById = await tiers.CurrentTiersAsync(list.Select(v => v.Id).ToList(), time.GetUtcNow(), ct);
            return Results.Ok(list.Select(v => new NearbyVenue(v.Id, v.Name, v.Address, v.Lat, v.Lng,
                tierById.GetValueOrDefault(v.Id, Tier.Default).ToString())));
        }).RequireAuthorization(AuthSetup.PlayerPolicy)
          .Doc("Venues", "Bares por perto", "Lista bares num raio ao redor de lat/lng via Google Places (cache de 1h por ~110 m). 422 invalid_location; 503 places_unavailable quando o Google falha ou a chave nao esta configurada.")
          .Produces<IEnumerable<NearbyVenue>>().PlayerProblems(422, 503);

        return app;
    }

    private static GeoPoint ParseLocation(string? lat, string? lng)
    {
        if (!TryParse(lat, out var la) || !TryParse(lng, out var lo) || la is < -90 or > 90 || lo is < -180 or > 180)
            throw new RuleViolation("invalid_location", "lat and lng are required and must be valid coordinates.");
        return new GeoPoint(la, lo);
    }

    private static bool TryParse(string? s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && double.IsFinite(v);
}
