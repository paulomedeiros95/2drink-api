using System.Net;
using Microsoft.EntityFrameworkCore;
using TwoDrink.Api.Tests.Infrastructure;
using TwoDrink.Domain.Scoring;
using TwoDrink.Infrastructure.Data.Entities;

namespace TwoDrink.Api.Tests;

// Only beer here: the hydration bonus arrives in Task 10 and would change the totals.
public class CheckInConfirmTests(ApiFactory factory) : CheckInTestBase(factory)
{
    [Fact]
    public async Task First_visit_scores_drinks_plus_default_badge()
    {
        var r = await Confirm(InsideBar, (DrinkType.Beer, 5));
        Assert.Equal(5m + 1m, r.PointsAwarded);
        Assert.Equal(Tier.Default, r.Badge!.Tier);
        Assert.Null(r.Badge.ArtUrl);
        Assert.Equal(1m, r.Badge.Points);
    }

    [Fact]
    public async Task Confirm_writes_check_in_badge_and_ledger_entries()
    {
        var r = await Confirm(InsideBar, (DrinkType.Beer, 2), (DrinkType.Beer, 3));

        await using var db = F.NewDb();
        var checkIn = await db.CheckIns.SingleAsync(c => c.Id == r.CheckInId);
        Assert.Equal(Me.UserId, checkIn.UserId);
        Assert.Equal(Me.LedgerAccountId, checkIn.LedgerAccountId);
        Assert.Equal(Bar, checkIn.VenueId);
        Assert.Equal(new Guid("5ea50000-2027-4000-8000-000000000001"), checkIn.SeasonId);
        Assert.Equal(Clock.GetUtcNow(), checkIn.CreatedAt);
        Assert.Equal(Clock.GetUtcNow().AddHours(12), checkIn.ComplementsUntil);
        Assert.Equal(5, checkIn.DrinkCount);
        Assert.Null(checkIn.ClosedAt);
        Assert.Equal($"final/{r.CheckInId}.jpg", checkIn.PhotoBlobName);

        var entries = await db.LedgerEntries.Where(e => e.AccountId == Me.LedgerAccountId).OrderBy(e => e.Sequence).ToListAsync();
        Assert.Collection(entries,
            e => { Assert.Equal(LedgerEntryType.CheckIn, e.Type); Assert.Equal(0m, e.Points); Assert.Equal(Bar, e.VenueId); Assert.Equal(r.CheckInId, e.CheckInId); },
            e => { Assert.Equal(LedgerEntryType.Drink, e.Type); Assert.Equal(DrinkType.Beer, e.DrinkType); Assert.Equal(5, e.Quantity); Assert.Equal(5m, e.Points); Assert.Equal(r.CheckInId, e.CheckInId); },
            e => { Assert.Equal(LedgerEntryType.Badge, e.Type); Assert.Equal(Tier.Default, e.Tier); Assert.Equal(1m, e.Points); Assert.Equal(r.CheckInId, e.CheckInId); });

        var badge = await db.UserBadges.SingleAsync(b => b.UserId == Me.UserId);
        Assert.Equal(Bar, badge.VenueId);
        Assert.Equal(Tier.Default, badge.Tier);
        Assert.Equal(r.CheckInId, badge.CheckInId);
        Assert.Equal(checkIn.SeasonId, badge.SeasonId);

        var intent = await db.CheckInIntents.SingleAsync(i => i.UserId == Me.UserId);
        Assert.Equal(r.CheckInId, intent.ConfirmedCheckInId);
    }

    [Fact]
    public async Task Second_visit_same_tier_scores_only_drinks()
    {
        await Confirm(InsideBar, (DrinkType.Beer, 1));
        Clock.Advance(TimeSpan.FromHours(16));
        var r = await Confirm(InsideBar, (DrinkType.Beer, 2));
        Assert.Equal(2m, r.PointsAwarded);
        Assert.Null(r.Badge);
    }

    [Fact]
    public async Task Tier_upgrade_grants_new_badge_with_new_points()
    {
        await Confirm(InsideBar, (DrinkType.Beer, 1));
        await Sponsor(Bar, Tier.Epic, artUrl: "https://cdn.example/epic.png");
        Clock.Advance(TimeSpan.FromHours(16));
        var r = await Confirm(InsideBar, (DrinkType.Beer, 1));
        Assert.Equal(51m, r.PointsAwarded);
        Assert.Equal(Tier.Epic, r.Badge!.Tier);
        Assert.Equal("https://cdn.example/epic.png", r.Badge.ArtUrl);
        Assert.Equal(50m, r.Badge.Points);
    }

    [Fact]
    public async Task Renewal_in_same_season_gives_no_new_badge_but_next_season_does()
    {
        await Sponsor(Bar, Tier.Epic);
        await Confirm(InsideBar, (DrinkType.Beer, 1)); // +50 +1
        await EndSponsorship(Bar);
        await Sponsor(Bar, Tier.Ouro);
        Clock.Advance(TimeSpan.FromHours(16));
        Assert.Null((await Confirm(InsideBar, (DrinkType.Beer, 1))).Badge); // same season

        Clock.SetUtcNow(DateTimeOffset.Parse("2027-07-02T20:00:00Z"));
        await SeedSeason("2027 – 2º semestre", new(2027, 7, 1), new(2027, 12, 31));
        Assert.Equal(Tier.Ouro, (await Confirm(InsideBar, (DrinkType.Beer, 1))).Badge!.Tier); // new season resets
    }

    [Fact]
    public async Task Badge_points_are_frozen_when_tier_later_drops()
    {
        await Sponsor(Bar, Tier.Epic);
        await Confirm(InsideBar, (DrinkType.Beer, 1));
        await EndSponsorship(Bar);
        Assert.Equal(51m, await LedgerTotal(Me));
    }

    [Fact]
    public async Task No_current_season_scores_drinks_without_badge()
    {
        // 2028-03-01 has no seeded season.
        Clock.SetUtcNow(DateTimeOffset.Parse("2028-03-01T20:00:00Z"));
        var r = await Confirm(InsideBar, (DrinkType.Beer, 3));
        Assert.Equal(3m, r.PointsAwarded);
        Assert.Null(r.Badge);
        await using var db = F.NewDb();
        Assert.Null((await db.CheckIns.SingleAsync(c => c.Id == r.CheckInId)).SeasonId);
    }

    [Fact]
    public async Task Zero_drinks_rejected()
    {
        var intent = await CreateIntent(Me, Bar, InsideBar);
        await Upload(intent.UploadUrl);

        var empty = await PostConfirm(Me, intent.IntentId, InsideBar);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
        Assert.Equal("drinks_required", await Code(empty));

        var zeroQty = await PostConfirm(Me, intent.IntentId, InsideBar, (DrinkType.Beer, 0));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, zeroQty.StatusCode);
        Assert.Equal("drinks_required", await Code(zeroQty));

        var missing = await PostConfirmRaw(Me, intent.IntentId, InsideBar, drinks: null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        Assert.Equal("drinks_required", await Code(missing));

        Assert.Equal(0, await CheckInCount(Me));
    }

    [Fact]
    public async Task Sixteen_drinks_rejected()
    {
        var intent = await CreateIntent(Me, Bar, InsideBar);
        await Upload(intent.UploadUrl);
        var res = await PostConfirm(Me, intent.IntentId, InsideBar, (DrinkType.Beer, 10), (DrinkType.Beer, 6));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("drink_limit", await Code(res));
        Assert.Equal(0, await CheckInCount(Me));
    }

    [Fact]
    public async Task Fifteen_drinks_accepted()
    {
        var r = await Confirm(InsideBar, (DrinkType.Beer, 15));
        Assert.Equal(16m, r.PointsAwarded);
    }

    [Fact]
    public async Task Unknown_drink_type_rejected()
    {
        var intent = await CreateIntent(Me, Bar, InsideBar);
        await Upload(intent.UploadUrl);
        foreach (var type in new object[] { "Absinthe", "1", "" })
        {
            var res = await PostConfirmRaw(Me, intent.IntentId, InsideBar, new[] { new { type, quantity = 1 } });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
            Assert.Equal("invalid_drinks", await Code(res));
        }
        Assert.Equal(0, await CheckInCount(Me));
    }

    [Fact]
    public async Task Confirm_without_uploaded_photo_rejected()
    {
        var intent = await CreateIntent(Me, Bar, InsideBar);
        var res = await PostConfirm(Me, intent.IntentId, InsideBar, (DrinkType.Beer, 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("photo_missing", await Code(res));
        Assert.Equal(0, await CheckInCount(Me));
    }

    [Fact]
    public async Task Photo_that_is_not_jpeg_rejected()
    {
        var intent = await CreateIntent(Me, Bar, InsideBar);
        await Upload(intent.UploadUrl, contentType: "image/png");
        var res = await PostConfirm(Me, intent.IntentId, InsideBar, (DrinkType.Beer, 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("photo_invalid", await Code(res));
        Assert.Equal(0, await CheckInCount(Me));
    }

    [Fact]
    public async Task Photo_larger_than_5_mb_rejected()
    {
        var intent = await CreateIntent(Me, Bar, InsideBar);
        await Upload(intent.UploadUrl, new byte[5 * 1024 * 1024 + 1]);
        var res = await PostConfirm(Me, intent.IntentId, InsideBar, (DrinkType.Beer, 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("photo_invalid", await Code(res));
        Assert.Equal(0, await CheckInCount(Me));
    }

    [Fact]
    public async Task Expired_intent_rejected()
    {
        var intent = await CreateIntent(Me, Bar, InsideBar);
        await Upload(intent.UploadUrl);
        Clock.Advance(TimeSpan.FromMinutes(11));
        var res = await PostConfirm(Me, intent.IntentId, InsideBar, (DrinkType.Beer, 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("intent_expired", await Code(res));
        Assert.Equal(0, await CheckInCount(Me));
    }

    [Fact]
    public async Task Unknown_intent_is_404()
    {
        var res = await PostConfirm(Me, Guid.NewGuid(), InsideBar, (DrinkType.Beer, 1));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("intent_not_found", await Code(res));
    }

    [Fact]
    public async Task Another_users_intent_is_404()
    {
        var intent = await CreateIntent(Me, Bar, InsideBar);
        await Upload(intent.UploadUrl);
        var other = await NewPlayer();
        var res = await PostConfirm(other, intent.IntentId, InsideBar, (DrinkType.Beer, 1));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("intent_not_found", await Code(res));
        Assert.Equal(0, await CheckInCount(other));
        Assert.Equal(0, await CheckInCount(Me));
    }

    [Fact]
    public async Task Double_confirm_is_idempotent()
    {
        var intent = await CreateIntent(Me, Bar, InsideBar);
        await Upload(intent.UploadUrl);

        var a = PostConfirm(Me, intent.IntentId, InsideBar, (DrinkType.Beer, 1));
        var b = PostConfirm(Me, intent.IntentId, InsideBar, (DrinkType.Beer, 1));
        await Task.WhenAll(a, b);
        var ra = await ReadConfirm(await a);
        var rb = await ReadConfirm(await b);

        Assert.Equal(ra.CheckInId, rb.CheckInId);
        Assert.Equal(2m, ra.PointsAwarded);
        Assert.Equal(2m, rb.PointsAwarded);
        Assert.Equal(ra.Badge, rb.Badge);
        Assert.Equal(1, await CheckInCount(Me));
        Assert.Equal(2m, await LedgerTotal(Me)); // 1 drink + badge 1

        // A late retry (after the intent expired) still returns the same result.
        Clock.Advance(TimeSpan.FromMinutes(30));
        var rc = await ReadConfirm(await PostConfirm(Me, intent.IntentId, InsideBar, (DrinkType.Beer, 1)));
        Assert.Equal(ra, rc);
        Assert.Equal(1, await CheckInCount(Me));
    }

    [Fact]
    public async Task Moving_out_of_range_between_intent_and_confirm_rejected()
    {
        var intent = await CreateIntent(Me, Bar, InsideBar);
        await Upload(intent.UploadUrl);
        var res = await PostConfirm(Me, intent.IntentId, FarAway, (DrinkType.Beer, 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("out_of_range", await Code(res));
        Assert.Equal(0, await CheckInCount(Me));
    }

    [Fact]
    public async Task Cooldown_is_rechecked_at_confirm()
    {
        // Two intents at two bars; confirming the first starts the 1h any-venue cooldown for the second.
        var otherBar = await AddVenue();
        var first = await CreateIntent(Me, Bar, InsideBar);
        var second = await CreateIntent(Me, otherBar, InsideBar);
        await Upload(first.UploadUrl);
        await Upload(second.UploadUrl);
        await ReadConfirm(await PostConfirm(Me, first.IntentId, InsideBar, (DrinkType.Beer, 1)));

        var res = await PostConfirm(Me, second.IntentId, InsideBar, (DrinkType.Beer, 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Equal("cooldown_any_venue", await Code(res));
        Assert.Equal(1, await CheckInCount(Me));
    }

    [Fact]
    public async Task Photo_is_frozen_at_confirm()
    {
        var intent = await CreateIntent(Me, Bar, InsideBar);
        await Upload(intent.UploadUrl);
        var r = await ReadConfirm(await PostConfirm(Me, intent.IntentId, InsideBar, (DrinkType.Beer, 1)));

        var container = F.SelfiesContainer();
        var final = container.GetBlobClient($"final/{r.CheckInId}.jpg");
        Assert.Equal(TinyJpeg, (await final.DownloadContentAsync()).Value.Content.ToArray());
        Assert.False((await container.GetBlobClient($"checkins/{intent.IntentId}.jpg").ExistsAsync()).Value);

        // The write SAS is still valid: overwriting the old slot must not touch the frozen selfie.
        byte[] gallery = [0xFF, 0xD8, 0x01, 0x02, 0x03, 0xFF, 0xD9];
        var put = await PutBlob(intent.UploadUrl, gallery);
        Assert.True(put.IsSuccessStatusCode || put.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.Equal(TinyJpeg, (await final.DownloadContentAsync()).Value.Content.ToArray());
    }

    [Fact]
    public async Task Storage_failure_at_confirm_is_503_and_writes_nothing()
    {
        var intent = await CreateIntent(Me, Bar, InsideBar);
        await Upload(intent.UploadUrl);
        F.PhotoStorage.FailCopy = true;

        var res = await PostConfirm(Me, intent.IntentId, InsideBar, (DrinkType.Beer, 1));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Equal("storage_unavailable", await Code(res));
        Assert.Equal(0, await CheckInCount(Me));
        Assert.Equal(0m, await LedgerTotal(Me));

        // Storage is back: the same intent can still be confirmed.
        F.PhotoStorage.FailCopy = false;
        var r = await ReadConfirm(await PostConfirm(Me, intent.IntentId, InsideBar, (DrinkType.Beer, 1)));
        Assert.Equal(2m, r.PointsAwarded);
    }

    [Fact]
    public async Task Confirm_requires_player()
    {
        var res = await F.CreateClient().PostAsync($"/checkins/intents/{Guid.NewGuid()}/confirm", null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
