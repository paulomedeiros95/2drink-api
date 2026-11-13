using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TwoDrink.Api.Jobs;
using TwoDrink.Api.Tests.Infrastructure;
using TwoDrink.Domain.Presence;
using TwoDrink.Domain.Scoring;
using TwoDrink.Infrastructure.Data.Entities;

namespace TwoDrink.Api.Tests;

public record ComplementResult(decimal PointsAwarded, int DrinkCount);

public class CheckInComplementTests(ApiFactory factory) : CheckInTestBase(factory)
{
    /// <summary>About <paramref name="meters"/> north of the bar.</summary>
    private static GeoPoint North(double meters) => new(BarLat + meters / 111_320.0, BarLng);

    private async Task<HttpResponseMessage> ComplementRaw(PlayerClient p, ConfirmResult c, GeoPoint at, object? drinks)
    {
        await F.ReauthenticateAsync(p);
        return await p.Http.PostAsJsonAsync($"/checkins/{c.CheckInId}/drinks",
            new { lat = at.Lat, lng = at.Lng, accuracyM = 10, isMocked = false, drinks });
    }

    private Task<HttpResponseMessage> ComplementRaw(ConfirmResult c, GeoPoint at, params (DrinkType Type, int Quantity)[] lines) =>
        ComplementRaw(Me, c, at, lines.Select(l => new { type = l.Type.ToString(), quantity = l.Quantity }).ToArray());

    private async Task<ComplementResult> Complement(ConfirmResult c, GeoPoint at, params (DrinkType Type, int Quantity)[] lines)
    {
        var res = await ComplementRaw(c, at, lines);
        Assert.True(res.StatusCode == HttpStatusCode.OK, $"complement returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<ComplementResult>(Json))!;
    }

    private async Task<CheckIn> LoadCheckIn(Guid id)
    {
        await using var db = F.NewDb();
        return await db.CheckIns.AsNoTracking().SingleAsync(c => c.Id == id);
    }

    private async Task Sweep()
    {
        using var scope = F.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CheckInCloser>().SweepAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Complement_adds_drinks_without_new_photo()
    {
        // The brief's example (2 beers + 1 water) would also meet the goal and add +2; here the goal is already met.
        var c = await Confirm(InsideBar, (DrinkType.Beer, 1), (DrinkType.Water, 1));
        Clock.Advance(TimeSpan.FromHours(2));
        var r = await Complement(c, InsideBar, (DrinkType.Water, 1));
        Assert.Equal(1.5m, r.PointsAwarded);
        Assert.Equal(3, r.DrinkCount);
        Assert.Equal(3, (await LoadCheckIn(c.CheckInId)).DrinkCount);
    }

    [Fact]
    public async Task Complement_out_of_range_rejected_and_checkin_stays_open()
    {
        var c = await Confirm(InsideBar, (DrinkType.Beer, 1));
        await AssertProblem(await ComplementRaw(c, North(300), (DrinkType.Beer, 1)), 422, "out_of_range");
        Assert.Null((await LoadCheckIn(c.CheckInId)).ClosedAt);
        Assert.Equal(200, (int)(await ComplementRaw(c, InsideBar, (DrinkType.Beer, 1))).StatusCode);
    }

    [Fact]
    public async Task Complement_mocked_location_rejected()
    {
        var c = await Confirm(InsideBar, (DrinkType.Beer, 1));
        await F.ReauthenticateAsync(Me);
        var res = await Me.Http.PostAsJsonAsync($"/checkins/{c.CheckInId}/drinks",
            new { lat = InsideBar.Lat, lng = InsideBar.Lng, accuracyM = 10, isMocked = true, drinks = new[] { new { type = "Beer", quantity = 1 } } });
        await AssertProblem(res, 422, "mock_location");
    }

    [Fact]
    public async Task Complement_cannot_exceed_15_total()
    {
        var c = await Confirm(InsideBar, (DrinkType.Beer, 14));
        await AssertProblem(await ComplementRaw(c, InsideBar, (DrinkType.Beer, 2)), 422, "drink_limit");
        Assert.Equal(14, (await LoadCheckIn(c.CheckInId)).DrinkCount);
        Assert.Equal(15, (await Complement(c, InsideBar, (DrinkType.Beer, 1))).DrinkCount);
    }

    [Fact]
    public async Task Complement_validates_drinks()
    {
        var c = await Confirm(InsideBar, (DrinkType.Beer, 1));
        await AssertProblem(await ComplementRaw(Me, c, InsideBar, Array.Empty<object>()), 422, "drinks_required");
        await AssertProblem(await ComplementRaw(Me, c, InsideBar, new[] { new { type = "Mead", quantity = 1 } }), 422, "invalid_drinks");
    }

    [Fact]
    public async Task Complement_merges_same_type_lines()
    {
        var c = await Confirm(InsideBar, (DrinkType.Beer, 1), (DrinkType.Water, 1));
        var r = await Complement(c, InsideBar, (DrinkType.Soda, 1), (DrinkType.Soda, 2));
        Assert.Equal(4.5m, r.PointsAwarded);
        await using var db = F.NewDb();
        var soda = await db.LedgerEntries.Where(e => e.CheckInId == c.CheckInId && e.DrinkType == DrinkType.Soda).ToListAsync();
        Assert.Equal(3, Assert.Single(soda).Quantity);
    }

    [Fact]
    public async Task Complement_after_12h_rejected()
    {
        var c = await Confirm(InsideBar, (DrinkType.Beer, 1));
        Clock.Advance(TimeSpan.FromHours(12));
        await AssertProblem(await ComplementRaw(c, InsideBar, (DrinkType.Beer, 1)), 422, "checkin_closed");
    }

    [Fact]
    public async Task Complement_just_before_12h_accepted()
    {
        var c = await Confirm(InsideBar, (DrinkType.Beer, 1));
        Clock.Advance(TimeSpan.FromHours(12) - TimeSpan.FromSeconds(1));
        Assert.Equal(2, (await Complement(c, InsideBar, (DrinkType.Beer, 1))).DrinkCount);
    }

    [Fact]
    public async Task New_checkin_elsewhere_closes_previous()
    {
        var first = await Confirm(InsideBar, (DrinkType.Beer, 1));
        Clock.Advance(TimeSpan.FromHours(2));
        var bar2 = await AddVenue();
        await F.ReauthenticateAsync(Me);
        var intent = await CreateIntent(Me, bar2, InsideBar);
        await Upload(intent.UploadUrl);
        await ReadConfirm(await PostConfirm(Me, intent.IntentId, InsideBar, (DrinkType.Beer, 1)));

        Assert.NotNull((await LoadCheckIn(first.CheckInId)).ClosedAt);
        await AssertProblem(await ComplementRaw(first, InsideBar, (DrinkType.Beer, 1)), 422, "checkin_closed");
    }

    [Fact]
    public async Task Water_only_checkin_gets_bonus_immediately()
    {
        var r = await Confirm(InsideBar, (DrinkType.Water, 5));
        Assert.Equal(7.5m + 1m + 2m, r.PointsAwarded); // 5 waters + Default badge + hydration
        Assert.True((await LoadCheckIn(r.CheckInId)).HydrationMet);
    }

    [Fact]
    public async Task Complement_that_meets_goal_gives_bonus_immediately()
    {
        var c = await Confirm(InsideBar, (DrinkType.Beer, 2));
        Assert.False((await LoadCheckIn(c.CheckInId)).HydrationMet);
        var r = await Complement(c, InsideBar, (DrinkType.Water, 1));
        Assert.Equal(1.5m + 2m, r.PointsAwarded);
        Assert.Equal(2m + 1m + 1.5m + 2m, await LedgerTotal(Me));
        Assert.True((await LoadCheckIn(c.CheckInId)).HydrationMet);
    }

    [Fact]
    public async Task Complement_that_breaks_goal_reverses_bonus_and_meeting_again_restores_it()
    {
        var c = await Confirm(InsideBar, (DrinkType.Beer, 2), (DrinkType.Water, 1)); // goal met: +2
        var broke = await Complement(c, InsideBar, (DrinkType.Beer, 2));              // 4 beers, 1 water: -2
        Assert.Equal(2m - 2m, broke.PointsAwarded);
        Assert.Equal(4m + 1.5m + 1m, await LedgerTotal(Me));
        Assert.False((await LoadCheckIn(c.CheckInId)).HydrationMet);

        await Complement(c, InsideBar, (DrinkType.Water, 1));                         // 2 waters: +2 again
        Assert.Equal(4m + 3m + 1m + 2m, await LedgerTotal(Me));
        Assert.True((await LoadCheckIn(c.CheckInId)).HydrationMet);

        await using var db = F.NewDb();
        var entries = await db.LedgerEntries.Where(e => e.CheckInId == c.CheckInId).OrderBy(e => e.Sequence).ToListAsync();
        var bonuses = entries.Where(e => e.Type == LedgerEntryType.HydrationBonus).ToList();
        var reversal = Assert.Single(entries, e => e.Type == LedgerEntryType.Reversal);
        Assert.Equal(2, bonuses.Count);
        Assert.Equal(-2m, reversal.Points);
        Assert.Equal(bonuses[0].Id, reversal.ReversesEntryId);
        Assert.Equal("hydration_goal_lost", reversal.Reason);
    }

    [Fact]
    public async Task No_hydration_bonus_when_goal_missed()
    {
        var r = await Confirm(InsideBar, (DrinkType.Beer, 3));
        Assert.Equal(3m + 1m, r.PointsAwarded);
        await using var db = F.NewDb();
        Assert.False(await db.LedgerEntries.AnyAsync(e => e.AccountId == Me.LedgerAccountId && e.Type == LedgerEntryType.HydrationBonus));
        Assert.False((await LoadCheckIn(r.CheckInId)).HydrationMet);
    }

    [Fact]
    public async Task Sweep_closes_without_changing_points()
    {
        var c = await Confirm(InsideBar, (DrinkType.Beer, 1));
        var total = await LedgerTotal(Me);
        await Sweep();
        Assert.Null((await LoadCheckIn(c.CheckInId)).ClosedAt); // not expired yet
        Clock.Advance(TimeSpan.FromHours(12));
        await Sweep();
        var closedAt = (await LoadCheckIn(c.CheckInId)).ClosedAt;
        Assert.NotNull(closedAt);
        await Sweep();
        Assert.Equal(closedAt, (await LoadCheckIn(c.CheckInId)).ClosedAt);
        Assert.Equal(total, await LedgerTotal(Me));
    }

    [Fact]
    public async Task Complement_for_someone_elses_checkin_is_404()
    {
        var c = await Confirm(InsideBar, (DrinkType.Beer, 1));
        var other = await NewPlayer();
        await AssertProblem(await ComplementRaw(other, c, InsideBar, new[] { new { type = "Beer", quantity = 1 } }), 404, "checkin_not_found");
        await AssertProblem(await ComplementRaw(Me, new ConfirmResult(Guid.NewGuid(), 0, null), InsideBar,
            new[] { new { type = "Beer", quantity = 1 } }), 404, "checkin_not_found");
    }

    [Fact]
    public async Task Concurrent_complements_respect_the_15_drink_limit()
    {
        var c = await Confirm(InsideBar, (DrinkType.Beer, 1));
        await F.ReauthenticateAsync(Me);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Me.Http.PostAsJsonAsync($"/checkins/{c.CheckInId}/drinks",
            new { lat = InsideBar.Lat, lng = InsideBar.Lng, accuracyM = 10, isMocked = false, drinks = new[] { new { type = "Beer", quantity = 8 } } })));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        var failed = Assert.Single(results, r => r.StatusCode != HttpStatusCode.OK);
        await AssertProblem(failed, 422, "drink_limit");
        Assert.Equal(9, (await LoadCheckIn(c.CheckInId)).DrinkCount);
    }

    [Fact]
    public async Task Confirm_replay_keeps_the_original_result_after_complements()
    {
        await F.ReauthenticateAsync(Me);
        var intent = await CreateIntent(Me, Bar, InsideBar);
        await Upload(intent.UploadUrl);
        var original = await ReadConfirm(await PostConfirm(Me, intent.IntentId, InsideBar, (DrinkType.Beer, 2), (DrinkType.Water, 1)));
        Assert.Equal(2m + 1.5m + 1m + 2m, original.PointsAwarded);

        await Complement(original, InsideBar, (DrinkType.Beer, 2));  // breaks the goal: -2
        await Complement(original, InsideBar, (DrinkType.Water, 1)); // restores it: +2

        var replay = await ReadConfirm(await PostConfirm(Me, intent.IntentId, InsideBar, (DrinkType.Beer, 2), (DrinkType.Water, 1)));
        Assert.Equal(original.CheckInId, replay.CheckInId);
        Assert.Equal(original.PointsAwarded, replay.PointsAwarded);
        Assert.Equal(original.Badge!.Points, replay.Badge!.Points);
    }

    // ---- GET /me/ledger ----

    private async Task<JsonElement> GetLedger(PlayerClient p, string query = "")
    {
        var res = await p.Http.GetAsync("/me/ledger" + query);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Ledger_lists_own_entries_newest_first_without_hashes()
    {
        var c = await Confirm(InsideBar, (DrinkType.Beer, 2), (DrinkType.Water, 1));
        var other = await NewPlayer();
        await Confirm(other, InsideBar, (DrinkType.Beer, 1));

        var items = (await GetLedger(Me)).EnumerateArray().ToList();
        Assert.Equal(new[] { "HydrationBonus", "Badge", "Drink", "Drink", "CheckIn" }, items.Select(i => i.GetProperty("type").GetString()));
        var seqs = items.Select(i => i.GetProperty("sequence").GetInt64()).ToList();
        Assert.Equal(seqs.OrderByDescending(x => x), seqs);
        Assert.All(items, i => Assert.Equal(c.CheckInId, i.GetProperty("checkInId").GetGuid()));
        var drink = items.First(i => i.GetProperty("type").GetString() == "Drink" && i.GetProperty("drinkType").GetString() == "Water");
        Assert.Equal(1, drink.GetProperty("quantity").GetInt32());
        Assert.Equal(1.5m, drink.GetProperty("points").GetDecimal());
        Assert.Equal(Bar, drink.GetProperty("venueId").GetGuid());
        var names = items[0].EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(new[] { "checkInId", "createdAt", "drinkType", "points", "quantity", "reason", "sequence", "tier", "type", "venueId" }, names);
    }

    [Fact]
    public async Task Ledger_paginates_with_before_cursor_and_limit()
    {
        await Confirm(InsideBar, (DrinkType.Beer, 2), (DrinkType.Water, 1)); // 5 entries
        var page1 = (await GetLedger(Me, "?limit=2")).EnumerateArray().ToList();
        Assert.Equal(2, page1.Count);
        var cursor = page1[^1].GetProperty("sequence").GetInt64();
        var page2 = (await GetLedger(Me, $"?limit=2&before={cursor}")).EnumerateArray().ToList();
        Assert.Equal(2, page2.Count);
        Assert.True(page2[0].GetProperty("sequence").GetInt64() < cursor);
        var page3 = (await GetLedger(Me, $"?limit=2&before={page2[^1].GetProperty("sequence").GetInt64()}")).EnumerateArray().ToList();
        Assert.Single(page3);
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=201")]
    [InlineData("?limit=-1")]
    [InlineData("?before=0")]
    public async Task Ledger_rejects_bad_paging(string query)
    {
        await AssertProblem(await Me.Http.GetAsync("/me/ledger" + query), 422, "invalid_paging");
    }

    [Fact]
    public async Task Ledger_requires_authentication()
    {
        using var anon = F.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/me/ledger")).StatusCode);
    }

    // ---- admin-style corrections (spec 5.11) must not confuse the hydration balance ----

    /// <summary>Appends an admin-style Reversal of the check-in's entry of the given drink type, through the ledger writer.</summary>
    private async Task ReverseDrink(Guid checkInId, DrinkType type)
    {
        using var scope = F.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TwoDrink.Infrastructure.Data.AppDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<TwoDrink.Infrastructure.Ledger.LedgerWriter>();
        await using var tx = await db.Database.BeginTransactionAsync();
        var target = await db.LedgerEntries.SingleAsync(e => e.CheckInId == checkInId && e.Type == LedgerEntryType.Drink && e.DrinkType == type);
        await writer.AppendAsync(target.AccountId,
            [new TwoDrink.Infrastructure.Ledger.NewEntry(LedgerEntryType.Reversal, -target.Points, VenueId: target.VenueId,
                CheckInId: checkInId, ReversesEntryId: target.Id, Reason: "admin_correction", ActorAdmin: "admin")], CancellationToken.None);
        await tx.CommitAsync();
    }

    private async Task<List<LedgerEntry>> CheckInEntries(Guid checkInId)
    {
        await using var db = F.NewDb();
        return await db.LedgerEntries.Where(e => e.CheckInId == checkInId).OrderBy(e => e.Sequence).ToListAsync();
    }

    [Fact]
    public async Task Admin_reversal_of_a_beer_line_does_not_duplicate_or_throw_in_reconciliation()
    {
        var c = await Confirm(InsideBar, (DrinkType.Beer, 2), (DrinkType.Water, 1)); // bonus +2
        await ReverseDrink(c.CheckInId, DrinkType.Beer);                            // balance of all reversals = 0 now

        var r = await Complement(c, InsideBar, (DrinkType.Soda, 1));                // goal still met (1 water, 0 live beers)
        Assert.Equal(1.5m, r.PointsAwarded);
        var entries = await CheckInEntries(c.CheckInId);
        Assert.Single(entries, e => e.Type == LedgerEntryType.HydrationBonus);
        Assert.True((await LoadCheckIn(c.CheckInId)).HydrationMet);
    }

    [Fact]
    public async Task Reversed_water_no_longer_counts_toward_the_goal()
    {
        var c = await Confirm(InsideBar, (DrinkType.Beer, 1), (DrinkType.Water, 1)); // bonus +2
        await ReverseDrink(c.CheckInId, DrinkType.Water);

        var r = await Complement(c, InsideBar, (DrinkType.Soda, 1));                 // 0 live waters: bonus reversed
        Assert.Equal(1.5m - 2m, r.PointsAwarded);
        Assert.False((await LoadCheckIn(c.CheckInId)).HydrationMet);
        var entries = await CheckInEntries(c.CheckInId);
        var bonus = Assert.Single(entries, e => e.Type == LedgerEntryType.HydrationBonus);
        Assert.Contains(entries, e => e.Reason == "hydration_goal_lost" && e.ReversesEntryId == bonus.Id);

        // a fresh water brings the goal back
        Assert.Equal(1.5m + 2m, (await Complement(c, InsideBar, (DrinkType.Water, 1))).PointsAwarded);
    }

    [Fact]
    public async Task New_checkin_after_window_closes_previous_at_complements_until()
    {
        var first = await Confirm(InsideBar, (DrinkType.Beer, 1));
        var until = (await LoadCheckIn(first.CheckInId)).ComplementsUntil;
        Clock.Advance(TimeSpan.FromHours(20));
        var bar2 = await AddVenue();
        await F.ReauthenticateAsync(Me);
        var intent = await CreateIntent(Me, bar2, InsideBar);
        await Upload(intent.UploadUrl);
        await ReadConfirm(await PostConfirm(Me, intent.IntentId, InsideBar, (DrinkType.Beer, 1)));
        Assert.Equal(until, (await LoadCheckIn(first.CheckInId)).ClosedAt);
    }
}
