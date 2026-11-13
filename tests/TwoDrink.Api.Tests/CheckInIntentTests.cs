using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TwoDrink.Api.Tests.Infrastructure;
using TwoDrink.Domain.Presence;
using TwoDrink.Infrastructure.Data.Entities;
using TwoDrink.Infrastructure.Places;

namespace TwoDrink.Api.Tests;

public class CheckInIntentTests : IClassFixture<ApiFactory>
{
    // The bar sits at (-23.5505, -46.6333); InsideBar is ~11 m away, FarAway ~1.1 km.
    private const double BarLat = -23.5505, BarLng = -46.6333;
    private static readonly (double Lat, double Lng) InsideBar = (-23.5506, -46.6333);
    private static readonly (double Lat, double Lng) FarAway = (-23.5605, -46.6333);

    private readonly ApiFactory _f;
    private PlayerClient? _player;

    public CheckInIntentTests(ApiFactory factory)
    {
        _f = factory;
        _f.Places.Reset();
        _f.Clock.SetUtcNow(TestClock.DefaultStart);
    }

    private async Task<PlayerClient> Player() => _player ??= await _f.CreatePlayerClientAsync("intent-" + Guid.NewGuid().ToString("N"));

    private async Task<Venue> AddVenue(string placeId, double lat = BarLat, double lng = BarLng, DateTimeOffset? cachedAt = null)
    {
        await using var db = _f.NewDb();
        var v = new Venue
        {
            Id = Guid.NewGuid(), PlaceId = placeId, Name = "Bar", Address = "Rua", Lat = lat, Lng = lng,
            CachedAt = cachedAt ?? _f.Clock.GetUtcNow(),
        };
        db.Venues.Add(v);
        await db.SaveChangesAsync();
        return v;
    }

    private async Task<HttpResponseMessage> PostIntent(Guid venueId, (double Lat, double Lng) at, double accuracy = 10,
        bool mocked = false, HttpClient? http = null) =>
        await (http ?? (await Player()).Http).PostAsJsonAsync("/checkins/intents",
            new { venueId, lat = at.Lat, lng = at.Lng, accuracyM = accuracy, isMocked = mocked });

    private record IntentResponse(Guid IntentId, Uri UploadUrl, DateTimeOffset ExpiresAt);

    private async Task<IntentResponse> CreateIntent(Guid venueId, (double Lat, double Lng) at)
    {
        var res = await PostIntent(venueId, at);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<IntentResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    private static readonly byte[] TinyJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9];

    private static async Task<HttpResponseMessage> PutBlob(Uri url)
    {
        using var http = new HttpClient();
        var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent(TinyJpeg) };
        req.Content.Headers.ContentType = new("image/jpeg");
        req.Headers.Add("x-ms-blob-type", "BlockBlob");
        return await http.SendAsync(req);
    }

    private static async Task UploadJpeg(Uri url) => Assert.Equal(HttpStatusCode.Created, (await PutBlob(url)).StatusCode);

    private async Task SeedCheckIn(Guid venueId, DateTimeOffset createdAt)
    {
        var p = await Player();
        await using var db = _f.NewDb();
        db.CheckIns.Add(new CheckIn
        {
            Id = Guid.NewGuid(), UserId = p.UserId, LedgerAccountId = p.LedgerAccountId, VenueId = venueId,
            PhotoBlobName = "checkins/seed.jpg", CreatedAt = createdAt, ComplementsUntil = createdAt.AddHours(12), DrinkCount = 1,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string?> Code(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task Intent_outside_radius_is_422_out_of_range()
    {
        var v = await AddVenue("intent-far");
        var res = await PostIntent(v.Id, FarAway);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("out_of_range", await Code(res));
    }

    [Fact]
    public async Task Intent_with_mocked_location_is_422()
    {
        var v = await AddVenue("intent-mock");
        var res = await PostIntent(v.Id, InsideBar, mocked: true);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("mock_location", await Code(res));
    }

    [Fact]
    public async Task Intent_for_unknown_venue_is_404()
    {
        var res = await PostIntent(Guid.NewGuid(), InsideBar);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("venue_not_found", await Code(res));
    }

    [Fact]
    public async Task Intent_requires_player()
    {
        var res = await PostIntent(Guid.NewGuid(), InsideBar, http: _f.CreateClient());
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Intent_within_cooldown_is_422()
    {
        var other = await AddVenue("intent-cd-other");
        var here = await AddVenue("intent-cd-here");
        await SeedCheckIn(other.Id, _f.Clock.GetUtcNow().AddMinutes(-30));
        var res = await PostIntent(here.Id, InsideBar);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("cooldown_any_venue", await Code(res));
    }

    [Fact]
    public async Task Intent_at_same_venue_within_cooldown_is_422_same_venue()
    {
        var here = await AddVenue("intent-cd-same");
        await SeedCheckIn(here.Id, _f.Clock.GetUtcNow().AddHours(-5));
        var res = await PostIntent(here.Id, InsideBar);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("cooldown_same_venue", await Code(res));
    }

    [Fact]
    public async Task Intent_returns_writable_url_for_its_blob_only()
    {
        var v = await AddVenue("intent-ok");
        var r = await CreateIntent(v.Id, InsideBar);
        Assert.Contains($"checkins/{r.IntentId}.jpg", r.UploadUrl.ToString());
        Assert.Equal(_f.Clock.GetUtcNow().AddMinutes(10), r.ExpiresAt);
        await UploadJpeg(r.UploadUrl);

        await using var db = _f.NewDb();
        var intent = await db.CheckInIntents.SingleAsync(i => i.Id == r.IntentId);
        Assert.Equal($"checkins/{r.IntentId}.jpg", intent.BlobName);
        Assert.Equal((await Player()).UserId, intent.UserId);
        Assert.Equal(v.Id, intent.VenueId);
        Assert.Equal(r.ExpiresAt, intent.ExpiresAt);
        Assert.Null(intent.ConfirmedCheckInId);
    }

    [Fact]
    public async Task Upload_url_cannot_read_or_write_another_blob()
    {
        var v = await AddVenue("intent-sas");
        var r = await CreateIntent(v.Id, InsideBar);
        await UploadJpeg(r.UploadUrl);

        using var http = new HttpClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(r.UploadUrl)).StatusCode);

        var otherPath = r.UploadUrl.AbsolutePath.Replace(r.IntentId.ToString(), Guid.NewGuid().ToString());
        var other = new UriBuilder(r.UploadUrl) { Path = otherPath }.Uri;
        Assert.Equal(HttpStatusCode.Forbidden, (await PutBlob(other)).StatusCode);
    }

    [Fact]
    public async Task Intent_refreshes_stale_venue_before_presence_check()
    {
        // Cached position is stale and wrong; Places says the bar is where the user stands.
        var v = await AddVenue("intent-stale", lat: -10, lng: -10, cachedAt: _f.Clock.GetUtcNow().AddDays(-60));
        _f.Places.DetailsResult = id => new PlaceResult(id, "Bar", "Rua", new GeoPoint(BarLat, BarLng));
        await CreateIntent(v.Id, InsideBar);
    }
}
