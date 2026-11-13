using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TwoDrink.Domain.Presence;
using TwoDrink.Domain.Scoring;
using TwoDrink.Infrastructure.Data.Entities;

namespace TwoDrink.Api.Tests.Infrastructure;

public record IntentResult(Guid IntentId, Uri UploadUrl, DateTimeOffset ExpiresAt);

public record BadgeResult(Tier Tier, string? ArtUrl, decimal Points);

public record ConfirmResult(Guid CheckInId, decimal PointsAwarded, BadgeResult? Badge);

/// <summary>
/// Shared helpers for check-in tests: every test gets a fresh player (<see cref="Me"/>) and a fresh bar (<see cref="Bar"/>),
/// so cooldowns, badges and sponsorships never bleed between tests on the shared fixture.
/// </summary>
public abstract class CheckInTestBase : IClassFixture<ApiFactory>, IAsyncLifetime
{
    // The bar sits at (-23.5505, -46.6333); InsideBar is ~11 m away, FarAway ~1.1 km.
    protected const double BarLat = -23.5505, BarLng = -46.6333;
    protected static readonly GeoPoint InsideBar = new(-23.5506, -46.6333);
    protected static readonly GeoPoint FarAway = new(-23.5605, -46.6333);

    protected static readonly byte[] TinyJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9];

    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected readonly ApiFactory F;
    protected TestClock Clock => F.Clock;

    /// <summary>The player of this test (distinct subject per test).</summary>
    protected PlayerClient Me { get; private set; } = null!;

    /// <summary>A venue created for this test at (<see cref="BarLat"/>, <see cref="BarLng"/>).</summary>
    protected Guid Bar { get; private set; }

    protected CheckInTestBase(ApiFactory factory)
    {
        F = factory;
        F.Places.Reset();
        F.PhotoStorage.FailCopy = false;
        F.Clock.SetUtcNow(TestClock.DefaultStart);
    }

    public virtual async Task InitializeAsync()
    {
        Me = await NewPlayer();
        Bar = await AddVenue();
    }

    public virtual Task DisposeAsync() => Task.CompletedTask;

    protected Task<PlayerClient> NewPlayer() => F.CreatePlayerClientAsync("ci-" + Guid.NewGuid().ToString("N"));

    protected async Task<Guid> AddVenue(double lat = BarLat, double lng = BarLng)
    {
        await using var db = F.NewDb();
        var v = new Venue
        {
            Id = Guid.NewGuid(), PlaceId = "ci-" + Guid.NewGuid().ToString("N"), Name = "Bar", Address = "Rua",
            Lat = lat, Lng = lng, CachedAt = Clock.GetUtcNow(),
        };
        db.Venues.Add(v);
        await db.SaveChangesAsync();
        return v.Id;
    }

    // ---- intent / upload / confirm ----

    protected static async Task<HttpResponseMessage> PostIntent(PlayerClient p, Guid venueId, GeoPoint at, double accuracy = 10) =>
        await p.Http.PostAsJsonAsync("/checkins/intents",
            new { venueId, lat = at.Lat, lng = at.Lng, accuracyM = accuracy, isMocked = false });

    protected static async Task<IntentResult> CreateIntent(PlayerClient p, Guid venueId, GeoPoint at)
    {
        var res = await PostIntent(p, venueId, at);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<IntentResult>(Json))!;
    }

    protected static async Task<HttpResponseMessage> PutBlob(Uri url, byte[]? bytes = null, string contentType = "image/jpeg")
    {
        using var http = new HttpClient();
        var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent(bytes ?? TinyJpeg) };
        req.Content.Headers.ContentType = new(contentType);
        req.Headers.Add("x-ms-blob-type", "BlockBlob");
        return await http.SendAsync(req);
    }

    protected static async Task Upload(Uri url, byte[]? bytes = null, string contentType = "image/jpeg") =>
        Assert.Equal(HttpStatusCode.Created, (await PutBlob(url, bytes, contentType)).StatusCode);

    /// <summary>POSTs the confirm call; <paramref name="drinks"/> is sent as-is (e.g. <c>new[] { new { type = "Beer", quantity = 1 } }</c>).</summary>
    protected static async Task<HttpResponseMessage> PostConfirmRaw(PlayerClient p, Guid intentId, GeoPoint at, object? drinks,
        double accuracy = 10) =>
        await p.Http.PostAsJsonAsync($"/checkins/intents/{intentId}/confirm",
            new { lat = at.Lat, lng = at.Lng, accuracyM = accuracy, isMocked = false, drinks });

    protected static Task<HttpResponseMessage> PostConfirm(PlayerClient p, Guid intentId, GeoPoint at,
        params (DrinkType Type, int Quantity)[] lines) =>
        PostConfirmRaw(p, intentId, at, lines.Select(l => new { type = l.Type.ToString(), quantity = l.Quantity }).ToArray());

    protected static async Task<ConfirmResult> ReadConfirm(HttpResponseMessage res)
    {
        Assert.True(res.StatusCode == HttpStatusCode.OK, $"confirm returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<ConfirmResult>(Json))!;
    }

    /// <summary>
    /// Intent → selfie upload → confirm at <see cref="Bar"/>; asserts 200. Logs the player in again first, since tests
    /// move the clock past the 1h access-token lifetime.
    /// </summary>
    protected async Task<ConfirmResult> Confirm(PlayerClient p, GeoPoint at, params (DrinkType Type, int Quantity)[] lines)
    {
        await F.ReauthenticateAsync(p);
        var intent = await CreateIntent(p, Bar, at);
        await Upload(intent.UploadUrl);
        return await ReadConfirm(await PostConfirm(p, intent.IntentId, at, lines));
    }

    protected Task<ConfirmResult> Confirm(GeoPoint at, params (DrinkType Type, int Quantity)[] lines) => Confirm(Me, at, lines);

    protected static async Task<string?> Code(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    // ---- sponsorships / seasons (owner connection) ----

    protected async Task Sponsor(Guid venueId, Tier tier, DateTimeOffset? start = null, DateTimeOffset? end = null, string? artUrl = null)
    {
        var from = start ?? Clock.GetUtcNow();
        await using var db = F.NewOwnerDb();
        db.VenueSponsorships.Add(new VenueSponsorship
        {
            Id = Guid.NewGuid(), VenueId = venueId, Tier = tier, BadgeArtUrl = artUrl,
            StartsAt = from, EndsAt = end ?? from.AddDays(365), CreatedBy = "test",
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Ends now every sponsorship of the venue that is in force now.</summary>
    protected async Task EndSponsorship(Guid venueId)
    {
        var now = Clock.GetUtcNow();
        await using var db = F.NewOwnerDb();
        await db.VenueSponsorships
            .Where(s => s.VenueId == venueId && s.StartsAt <= now && now < s.EndsAt)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndsAt, now));
    }

    /// <summary>Adds a season unless one with the same dates exists (the fixture is shared across tests).</summary>
    protected async Task SeedSeason(string name, DateOnly start, DateOnly end)
    {
        await using var db = F.NewOwnerDb();
        if (await db.Seasons.AnyAsync(s => s.StartDate == start && s.EndDate == end)) return;
        db.Seasons.Add(new Season { Id = Guid.NewGuid(), Name = name, StartDate = start, EndDate = end });
        await db.SaveChangesAsync();
    }

    // ---- queries ----

    protected async Task<decimal> LedgerTotal(Guid ledgerAccountId)
    {
        await using var db = F.NewDb();
        return await db.LedgerEntries.Where(e => e.AccountId == ledgerAccountId).SumAsync(e => e.Points);
    }

    protected Task<decimal> LedgerTotal(PlayerClient p) => LedgerTotal(p.LedgerAccountId);

    protected async Task<int> CheckInCount(Guid userId)
    {
        await using var db = F.NewDb();
        return await db.CheckIns.CountAsync(c => c.UserId == userId);
    }

    protected Task<int> CheckInCount(PlayerClient p) => CheckInCount(p.UserId);
}
