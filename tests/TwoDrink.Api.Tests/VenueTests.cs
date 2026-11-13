using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TwoDrink.Api.Features.Seasons;
using TwoDrink.Api.Features.Venues;
using TwoDrink.Api.Tests.Infrastructure;
using TwoDrink.Domain.Errors;
using TwoDrink.Domain.Presence;
using TwoDrink.Domain.Scoring;
using TwoDrink.Infrastructure.Data.Entities;
using TwoDrink.Infrastructure.Places;

namespace TwoDrink.Api.Tests;

public class VenueTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _f;
    private PlayerClient? _player;

    public VenueTests(ApiFactory factory)
    {
        _f = factory;
        _f.Places.Reset();
        _f.Clock.SetUtcNow(TestClock.DefaultStart);
    }

    private async Task<PlayerClient> Player() => _player ??= await _f.CreatePlayerClientAsync("venue-player");

    private async Task<HttpResponseMessage> GetNearby(double lat, double lng) =>
        await (await Player()).Http.GetAsync(FormattableString.Invariant($"/venues/nearby?lat={lat}&lng={lng}"));

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> f)
    {
        await using var scope = _f.Services.CreateAsyncScope();
        return await f(scope.ServiceProvider);
    }

    private async Task<Venue> AddVenue(string placeId, DateTimeOffset? cachedAt = null)
    {
        await using var db = _f.NewDb();
        var v = new Venue
        {
            Id = Guid.NewGuid(), PlaceId = placeId, Name = "Old Name", Address = "Old", Lat = 1, Lng = 2,
            CachedAt = cachedAt ?? _f.Clock.GetUtcNow(),
        };
        db.Venues.Add(v);
        await db.SaveChangesAsync();
        return v;
    }

    private async Task AddSponsorship(Guid venueId, Tier tier, DateTimeOffset start, DateTimeOffset end, string? art = null)
    {
        await using var db = _f.NewDb();
        db.VenueSponsorships.Add(new VenueSponsorship
        {
            Id = Guid.NewGuid(), VenueId = venueId, Tier = tier, BadgeArtUrl = art, StartsAt = start, EndsAt = end, CreatedBy = "test",
        });
        await db.SaveChangesAsync();
    }

    private static DateTimeOffset Utc(int y, int m, int d, int h = 0) => new(y, m, d, h, 0, 0, TimeSpan.Zero);

    // ----- nearby endpoint -----

    [Fact]
    public async Task Nearby_upserts_venues_by_place_id()
    {
        _f.Places.NearbyResult = p => [new PlaceResult("up-1", "Bar Um", "Rua 1", new GeoPoint(p.Lat, p.Lng))];
        Assert.Equal(HttpStatusCode.OK, (await GetNearby(-10.0, -10.0)).StatusCode);
        // A different area misses the cache but returns the same place, with changed data.
        _f.Places.NearbyResult = p => [new PlaceResult("up-1", "Bar Um Renomeado", "Rua 2", new GeoPoint(p.Lat, p.Lng))];
        var res = await GetNearby(-11.0, -11.0);
        Assert.Equal(2, _f.Places.NearbyCalls);

        await using var db = _f.NewDb();
        var venue = await db.Venues.SingleAsync(v => v.PlaceId == "up-1");
        Assert.Equal("Bar Um Renomeado", venue.Name);
        Assert.Equal("Rua 2", venue.Address);
        Assert.Equal(-11.0, venue.Lat);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(venue.Id, body[0].GetProperty("venueId").GetGuid());
    }

    [Fact]
    public async Task Concurrent_upserts_of_same_place_yield_one_venue()
    {
        _f.Places.NearbyResult = p => [new PlaceResult("race-1", "Bar Race", null, new GeoPoint(p.Lat, p.Lng))];
        var calls = Enumerable.Range(0, 8).Select(i => GetNearby(-20.0 - i, -20.0)).ToArray();
        foreach (var r in await Task.WhenAll(calls)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        await using var db = _f.NewDb();
        Assert.Equal(1, await db.Venues.CountAsync(v => v.PlaceId == "race-1"));
    }

    [Fact]
    public async Task Nearby_reuses_cached_results_for_same_area()
    {
        await GetNearby(-23.55050, -46.63330);
        await GetNearby(-23.55052, -46.63331);
        Assert.Equal(1, _f.Places.NearbyCalls);
    }

    [Fact]
    public async Task Nearby_calls_places_again_for_a_different_area()
    {
        await GetNearby(-30.001, -50.001);
        await GetNearby(-30.003, -50.001);
        Assert.Equal(2, _f.Places.NearbyCalls);
    }

    [Fact]
    public async Task Nearby_returns_shape_and_current_tier()
    {
        _f.Places.NearbyResult = p => [new PlaceResult("tier-1", "Bar Epic", "Rua E", new GeoPoint(p.Lat, p.Lng))];
        var first = await (await GetNearby(-12.0, -12.0)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Default", first[0].GetProperty("tier").GetString());
        var id = first[0].GetProperty("venueId").GetGuid();

        await AddSponsorship(id, Tier.Epic, Utc(2027, 2, 1), Utc(2027, 2, 10));
        var body = await (await GetNearby(-12.0, -12.0)).Content.ReadFromJsonAsync<JsonElement>(); // cached places, fresh tier
        Assert.Equal(1, _f.Places.NearbyCalls);
        var v = body[0];
        Assert.Equal("Bar Epic", v.GetProperty("name").GetString());
        Assert.Equal("Rua E", v.GetProperty("address").GetString());
        Assert.Equal(-12.0, v.GetProperty("lat").GetDouble());
        Assert.Equal(-12.0, v.GetProperty("lng").GetDouble());
        Assert.Equal("Epic", v.GetProperty("tier").GetString());
    }

    [Fact]
    public async Task Nearby_requires_player()
    {
        var anon = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/venues/nearby?lat=1&lng=1")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?lat=1")]
    [InlineData("?lng=1")]
    [InlineData("?lat=abc&lng=1")]
    [InlineData("?lat=91&lng=1")]
    [InlineData("?lat=-91&lng=1")]
    [InlineData("?lat=1&lng=181")]
    [InlineData("?lat=1&lng=-181")]
    [InlineData("?lat=NaN&lng=1")]
    [InlineData("?lat=1&lng=Infinity")]
    public async Task Nearby_rejects_invalid_location(string query)
    {
        var res = await (await Player()).Http.GetAsync("/venues/nearby" + query);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_location", body.GetProperty("code").GetString());
        Assert.Equal(0, _f.Places.NearbyCalls);
    }

    [Fact]
    public async Task Nearby_returns_503_when_places_fails_without_leaking_details()
    {
        _f.Places.NearbyResult = _ => throw new PlacesUnavailableException("google said: secret detail");
        var res = await GetNearby(-40.0, -40.0);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        var text = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret", text);
        Assert.Equal("places_unavailable", JsonDocument.Parse(text).RootElement.GetProperty("code").GetString());
    }

    // ----- tiers -----

    [Fact]
    public async Task Tier_is_default_without_sponsorship()
    {
        var v = await AddVenue("tier-none");
        var (tier, art) = await InScope(sp => sp.GetRequiredService<VenueTierService>().CurrentAsync(v.Id, Utc(2027, 2, 5), default));
        Assert.Equal(Tier.Default, tier);
        Assert.Null(art);
    }

    [Fact]
    public async Task Tier_follows_sponsorship_window()
    {
        var v = await AddVenue("tier-window");
        await AddSponsorship(v.Id, Tier.Epic, Utc(2027, 2, 1), Utc(2027, 2, 10), "https://art/epic.png");

        async Task<(Tier, string?)> At(DateTimeOffset t) =>
            await InScope(sp => sp.GetRequiredService<VenueTierService>().CurrentAsync(v.Id, t, default));

        Assert.Equal((Tier.Epic, "https://art/epic.png"), await At(Utc(2027, 2, 5)));
        Assert.Equal((Tier.Epic, "https://art/epic.png"), await At(Utc(2027, 2, 1)));          // start inclusive
        Assert.Equal((Tier.Default, (string?)null), await At(Utc(2027, 2, 10)));                // end exclusive
        Assert.Equal((Tier.Default, (string?)null), await At(Utc(2027, 2, 11)));
        Assert.Equal((Tier.Default, (string?)null), await At(Utc(2027, 1, 31, 23)));
    }

    [Fact]
    public async Task Tier_picks_highest_when_sponsorships_overlap()
    {
        var v = await AddVenue("tier-overlap");
        await AddSponsorship(v.Id, Tier.Bronze, Utc(2027, 3, 1), Utc(2027, 3, 31), "bronze");
        await AddSponsorship(v.Id, Tier.Ouro, Utc(2027, 3, 10), Utc(2027, 3, 20), "ouro");
        var r = await InScope(sp => sp.GetRequiredService<VenueTierService>().CurrentAsync(v.Id, Utc(2027, 3, 15), default));
        Assert.Equal((Tier.Ouro, "ouro"), r);
    }

    // ----- freshness -----

    [Fact]
    public async Task Stale_venue_is_refreshed_after_30_days()
    {
        var now = _f.Clock.GetUtcNow();
        var v = await AddVenue("stale-1", cachedAt: now.AddDays(-31));
        _f.Places.DetailsResult = id => new PlaceResult(id, "Novo Nome", "Nova Rua", new GeoPoint(5, 6));

        var fresh = await InScope(sp => sp.GetRequiredService<VenueService>().EnsureFreshAsync(v, default));

        Assert.Equal(1, _f.Places.DetailsCalls);
        Assert.Equal("Novo Nome", fresh.Name);
        await using var db = _f.NewDb();
        var stored = await db.Venues.SingleAsync(x => x.Id == v.Id);
        Assert.Equal("Novo Nome", stored.Name);
        Assert.Equal("Nova Rua", stored.Address);
        Assert.Equal(5, stored.Lat);
        Assert.Equal(6, stored.Lng);
        Assert.Equal(now, stored.CachedAt);
    }

    [Fact]
    public async Task Recent_venue_is_not_refreshed()
    {
        var v = await AddVenue("fresh-1", cachedAt: _f.Clock.GetUtcNow().AddDays(-29));
        await InScope(sp => sp.GetRequiredService<VenueService>().EnsureFreshAsync(v, default));
        Assert.Equal(0, _f.Places.DetailsCalls);
    }

    [Fact]
    public async Task Stale_venue_survives_when_details_unavailable()
    {
        var v = await AddVenue("stale-2", cachedAt: _f.Clock.GetUtcNow().AddDays(-40));
        _f.Places.DetailsResult = _ => throw new PlacesUnavailableException("down");
        var r = await InScope(sp => sp.GetRequiredService<VenueService>().EnsureFreshAsync(v, default));
        Assert.Equal("Old Name", r.Name);
    }

    // ----- seasons -----

    [Fact]
    public async Task Season_boundaries_use_sao_paulo_dates()
    {
        async Task<string?> At(DateTimeOffset t) =>
            (await InScope(sp => sp.GetRequiredService<SeasonService>().AtAsync(t, default)))?.Name;

        Assert.Equal("2027 – 1º semestre", await At(Utc(2027, 2, 6, 23)));
        Assert.Equal("2026 – 2º semestre", await At(Utc(2027, 1, 1, 2)));   // 23:00 Dec 31 in SP
        Assert.Equal("2027 – 1º semestre", await At(Utc(2027, 1, 1, 3)));   // 00:00 Jan 1 in SP
        Assert.Equal("2026 – 2º semestre", await At(Utc(2026, 7, 1, 3)));   // 00:00 Jul 1 in SP
        Assert.Null(await At(Utc(2026, 7, 1, 2)));                          // 23:00 Jun 30 in SP: before season 1
    }

    [Fact]
    public async Task Overlapping_season_rejected()
    {
        var ex = await Assert.ThrowsAsync<RuleViolation>(() => InScope(sp =>
            sp.GetRequiredService<SeasonService>().CreateAsync("Overlap", new DateOnly(2027, 6, 1), new DateOnly(2027, 12, 31), default)));
        Assert.Equal("season_overlap", ex.Code);
    }

    [Fact]
    public async Task Season_with_start_after_end_rejected()
    {
        var ex = await Assert.ThrowsAsync<RuleViolation>(() => InScope(sp =>
            sp.GetRequiredService<SeasonService>().CreateAsync("Bad", new DateOnly(2028, 2, 1), new DateOnly(2028, 1, 1), default)));
        Assert.Equal("season_dates_invalid", ex.Code);
    }

    [Fact]
    public async Task Adjacent_season_is_created_and_one_day_overlap_rejected()
    {
        var s = await InScope(sp => sp.GetRequiredService<SeasonService>()
            .CreateAsync("2027 – 2º semestre", new DateOnly(2027, 7, 1), new DateOnly(2027, 12, 31), default));
        Assert.NotEqual(Guid.Empty, s.Id);
        var ex = await Assert.ThrowsAsync<RuleViolation>(() => InScope(sp =>
            sp.GetRequiredService<SeasonService>().CreateAsync("X", new DateOnly(2027, 12, 31), new DateOnly(2028, 3, 1), default)));
        Assert.Equal("season_overlap", ex.Code);
    }
}
