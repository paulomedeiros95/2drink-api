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
using TwoDrink.Infrastructure.Ledger;

namespace TwoDrink.Api.Tests;

public record StandingRow(Guid UserId, string DisplayName, decimal Points, int DistinctVenues, int Rank);

public class StandingsTests(ApiFactory factory) : CheckInTestBase(factory)
{
    // Default clock: 2027-02-06T23:00Z (Sao Paulo 20:00). Bar is the venue of the test; Bar2 is ~1.1 km away.
    static readonly DateOnly Feb1 = new(2027, 2, 1), Feb15 = new(2027, 2, 15), Feb28 = new(2027, 2, 28);

    Guid Bar2;
    static readonly GeoPoint InsideBar2 = new(-23.5606, -46.6333);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Bar2 = await AddVenue(-23.5605, -46.6333);
    }

    static async Task<Guid> CreateLeague(PlayerClient p, DateOnly start, DateOnly end, string name = "Liga")
    {
        var res = await p.Http.PostAsJsonAsync("/leagues", new { name, startDate = start, endDate = end });
        Assert.True(res.IsSuccessStatusCode, $"create returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    async Task Join(PlayerClient creator, PlayerClient joiner, Guid leagueId)
    {
        await F.ReauthenticateAsync(creator);
        await F.ReauthenticateAsync(joiner);
        var inv = await creator.Http.PostAsync($"/leagues/{leagueId}/invites", null);
        Assert.Equal(HttpStatusCode.OK, inv.StatusCode);
        var token = (await inv.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var acc = await joiner.Http.PostAsync($"/invites/{token}/accept", null);
        Assert.True(acc.IsSuccessStatusCode, $"accept returned {(int)acc.StatusCode}: {await acc.Content.ReadAsStringAsync()}");
    }

    async Task<HttpResponseMessage> GetStandings(PlayerClient p, Guid leagueId)
    {
        await F.ReauthenticateAsync(p);
        return await p.Http.GetAsync($"/leagues/{leagueId}/standings");
    }

    async Task<List<StandingRow>> Standings(PlayerClient p, Guid leagueId)
    {
        var res = await GetStandings(p, leagueId);
        Assert.True(res.StatusCode == HttpStatusCode.OK, $"standings returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<List<StandingRow>>(Json))!;
    }

    async Task<ConfirmResult> ConfirmAt(PlayerClient p, Guid venue, GeoPoint at, params (DrinkType Type, int Quantity)[] lines)
    {
        await F.ReauthenticateAsync(p);
        var intent = await CreateIntent(p, venue, at);
        await Upload(intent.UploadUrl);
        return await ReadConfirm(await PostConfirm(p, intent.IntentId, at, lines));
    }

    static readonly (DrinkType, int) OneBeer = (DrinkType.Beer, 1);

    static TrophyAwarder AwarderIn(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<TrophyAwarder>();

    async Task AwardDue()
    {
        using var scope = F.Services.CreateScope();
        await AwarderIn(scope).AwardDueAsync(CancellationToken.None);
    }

    async Task<int> TrophyCount(Guid leagueId)
    {
        await using var db = F.NewDb();
        return await db.Trophies.CountAsync(t => t.LeagueId == leagueId);
    }

    // ---- window ----

    [Fact]
    public async Task Points_before_joining_do_not_count()
    {
        var b = await NewPlayer();
        var league = await CreateLeague(Me, Feb1, Feb28);
        await ConfirmAt(b, Bar, InsideBar, OneBeer);          // before joining (clock 2027-02-06T23:00Z)
        Clock.Advance(TimeSpan.FromHours(2));
        await Join(Me, b, league);
        Clock.Advance(TimeSpan.FromHours(2));
        var after = await ConfirmAt(b, Bar2, InsideBar2, OneBeer);

        var rows = await Standings(Me, league);
        var row = Assert.Single(rows, r => r.UserId == b.UserId);
        Assert.Equal(after.PointsAwarded, row.Points);
        Assert.True(await LedgerTotal(b) > row.Points);
        Assert.Equal(1, row.DistinctVenues);
    }

    [Fact]
    public async Task Checkin_at_2330_on_last_day_counts_and_0001_next_day_does_not()
    {
        var league = await CreateLeague(Me, Feb1, Feb15);
        Clock.SetUtcNow(DateTimeOffset.Parse("2027-02-15T23:30:00-03:00").ToUniversalTime());
        var inside = await ConfirmAt(Me, Bar, InsideBar, OneBeer);
        Clock.SetUtcNow(DateTimeOffset.Parse("2027-02-16T00:31:00-03:00").ToUniversalTime());
        var outside = await ConfirmAt(Me, Bar2, InsideBar2, OneBeer);
        Assert.True(outside.PointsAwarded > 0);

        var row = Assert.Single(await Standings(Me, league));
        Assert.Equal(inside.PointsAwarded, row.Points);
        Assert.Equal(1, row.DistinctVenues);
        Assert.Equal(inside.PointsAwarded + outside.PointsAwarded, await LedgerTotal(Me));
    }

    [Fact]
    public async Task Same_checkin_scores_in_both_leagues()
    {
        var l1 = await CreateLeague(Me, Feb1, Feb28, "Um");
        var l2 = await CreateLeague(Me, Feb1, Feb28, "Dois");
        var c = await Confirm(InsideBar, (DrinkType.Beer, 2));
        Assert.Equal(c.PointsAwarded, Assert.Single(await Standings(Me, l1)).Points);
        Assert.Equal(c.PointsAwarded, Assert.Single(await Standings(Me, l2)).Points);
    }

    [Fact]
    public async Task Member_who_left_is_not_ranked()
    {
        var b = await NewPlayer();
        var league = await CreateLeague(Me, Feb1, Feb28);
        await Join(Me, b, league);
        await ConfirmAt(b, Bar, InsideBar, OneBeer);
        Assert.Equal(2, (await Standings(Me, league)).Count);

        await F.ReauthenticateAsync(b);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Http.PostAsync($"/leagues/{league}/leave", null)).StatusCode);

        var rows = await Standings(Me, league);
        Assert.Equal(Me.UserId, Assert.Single(rows).UserId);
        Assert.Equal(HttpStatusCode.NotFound, (await GetStandings(b, league)).StatusCode);
    }

    [Fact]
    public async Task Reversal_reduces_points()
    {
        var league = await CreateLeague(Me, Feb1, Feb28);
        var c = await Confirm(InsideBar, (DrinkType.Beer, 2), (DrinkType.Water, 1)); // hydration bonus +2
        var before = Assert.Single(await Standings(Me, league)).Points;
        Assert.Equal(c.PointsAwarded, before);

        using (var scope = F.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TwoDrink.Infrastructure.Data.AppDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<LedgerWriter>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var target = await db.LedgerEntries.SingleAsync(e => e.CheckInId == c.CheckInId && e.Type == LedgerEntryType.Drink && e.DrinkType == DrinkType.Beer);
            await writer.AppendAsync(target.AccountId,
                [new NewEntry(LedgerEntryType.Reversal, -target.Points, VenueId: target.VenueId, CheckInId: c.CheckInId,
                    ReversesEntryId: target.Id, Reason: "admin_correction", ActorAdmin: "admin")], CancellationToken.None);
            await tx.CommitAsync();
        }

        var row = Assert.Single(await Standings(Me, league));
        Assert.True(row.Points < before);
        Assert.Equal(await LedgerTotal(Me), row.Points);
    }

    // ---- ranking and visibility ----

    [Fact]
    public async Task Ranking_orders_by_points_then_venues_and_shares_rank_on_full_tie()
    {
        var b = await NewPlayer();
        var c = await NewPlayer();
        var d = await NewPlayer();
        var league = await CreateLeague(Me, Feb1, Feb28);
        await Join(Me, b, league);
        await Join(Me, c, league);
        await Join(Me, d, league);
        // b and c do the same thing (a tie); Me scores more; d scores nothing.
        await ConfirmAt(Me, Bar, InsideBar, (DrinkType.Beer, 5));
        await ConfirmAt(b, Bar, InsideBar, OneBeer);
        await ConfirmAt(c, Bar, InsideBar, OneBeer);

        var rows = await Standings(Me, league);
        Assert.Equal([1, 2, 2, 4], rows.Select(r => r.Rank));
        Assert.Equal(Me.UserId, rows[0].UserId);
        Assert.Equal(d.UserId, rows[3].UserId);
        Assert.Equal(0m, rows[3].Points);
        Assert.Equal(0, rows[3].DistinctVenues);
        // Ties are listed by display name (subject), then user id.
        Assert.True(string.CompareOrdinal(rows[1].DisplayName, rows[2].DisplayName) <= 0);
    }

    [Fact]
    public async Task Non_member_and_unknown_league_get_404_not_member()
    {
        var outsider = await NewPlayer();
        var league = await CreateLeague(Me, Feb1, Feb28);
        await AssertProblem(await GetStandings(outsider, league), 404, "not_member");
        await AssertProblem(await GetStandings(outsider, Guid.NewGuid()), 404, "not_member");
    }

    [Fact]
    public async Task Upcoming_league_shows_everyone_with_zero()
    {
        var b = await NewPlayer();
        var league = await CreateLeague(Me, new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 10));
        await Join(Me, b, league);
        await Confirm(InsideBar, (DrinkType.Beer, 1));
        var rows = await Standings(Me, league);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => { Assert.Equal(0m, r.Points); Assert.Equal(0, r.DistinctVenues); Assert.Equal(1, r.Rank); });
    }

    // ---- trophies ----

    [Fact]
    public async Task Trophies_awarded_once_at_league_end()
    {
        var b = await NewPlayer();
        var league = await CreateLeague(Me, Feb1, Feb15);
        await Join(Me, b, league);
        await ConfirmAt(Me, Bar, InsideBar, (DrinkType.Beer, 3));
        await ConfirmAt(b, Bar, InsideBar, OneBeer);

        Clock.SetUtcNow(DateTimeOffset.Parse("2027-02-16T03:01:59Z"));
        await AwardDue();
        Assert.Equal(0, await TrophyCount(league));          // inside the 2 min grace

        Clock.SetUtcNow(DateTimeOffset.Parse("2027-02-16T03:02:01Z"));
        await AwardDue();
        await AwardDue();
        Assert.Equal(1, await TrophyCount(league));
        await using var db = F.NewDb();
        var trophy = await db.Trophies.SingleAsync(t => t.LeagueId == league);
        Assert.Equal(Me.UserId, trophy.UserId);
        Assert.Equal(Clock.GetUtcNow(), trophy.AwardedAt);
        Assert.NotNull((await db.Leagues.SingleAsync(l => l.Id == league)).TrophiesAwardedAt);
    }

    [Fact]
    public async Task Full_tie_awards_every_champion()
    {
        var b = await NewPlayer();
        var league = await CreateLeague(Me, Feb1, Feb15);
        await Join(Me, b, league);
        await ConfirmAt(Me, Bar, InsideBar, OneBeer);
        await ConfirmAt(b, Bar, InsideBar, OneBeer);
        Clock.SetUtcNow(DateTimeOffset.Parse("2027-02-16T03:02:01Z"));
        await AwardDue();
        Assert.Equal(2, await TrophyCount(league));
    }

    [Fact]
    public async Task League_without_champion_is_marked_and_gets_no_trophy()
    {
        var league = await CreateLeague(Me, Feb1, Feb15);
        Clock.SetUtcNow(DateTimeOffset.Parse("2027-02-16T03:02:01Z"));
        await AwardDue();
        Assert.Equal(0, await TrophyCount(league));
        await using var db = F.NewDb();
        Assert.NotNull((await db.Leagues.SingleAsync(l => l.Id == league)).TrophiesAwardedAt);
    }

    [Fact]
    public async Task Concurrent_awarders_create_one_set_of_trophies()
    {
        var league = await CreateLeague(Me, Feb1, Feb15);
        await Confirm(InsideBar, (DrinkType.Beer, 1));
        Clock.SetUtcNow(DateTimeOffset.Parse("2027-02-16T03:02:01Z"));
        // The fixture is shared, so other ended leagues of this class may also be due: compare against what changed.
        async Task<HashSet<Guid>> Unawarded()
        {
            await using var q = F.NewDb();
            return (await q.Leagues.Where(l => l.TrophiesAwardedAt == null).Select(l => l.Id).ToListAsync()).ToHashSet();
        }
        var before = await Unawarded();
        var counts = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var scope = F.Services.CreateScope();
            return await AwarderIn(scope).AwardDueAsync(CancellationToken.None);
        }));
        var newlyMarked = before.Except(await Unawarded()).ToList();
        Assert.Contains(league, newlyMarked);
        // Each league is awarded by exactly one of the four calls, and none of them failed (failures are swallowed and logged).
        Assert.Equal(newlyMarked.Count, counts.Sum());
        Assert.Equal(1, await TrophyCount(league));
    }

    [Fact]
    public async Task Leader_who_leaves_the_ended_league_before_the_award_still_gets_the_trophy()
    {
        var b = await NewPlayer();
        var league = await CreateLeague(Me, Feb1, Feb15);
        await Join(Me, b, league);
        await ConfirmAt(Me, Bar, InsideBar, (DrinkType.Beer, 3));
        await ConfirmAt(b, Bar, InsideBar, OneBeer);

        Clock.SetUtcNow(DateTimeOffset.Parse("2027-02-16T03:00:30Z"));   // ended, award has not run yet
        await F.ReauthenticateAsync(Me);
        Assert.Equal(HttpStatusCode.NoContent, (await Me.Http.PostAsync($"/leagues/{league}/leave", null)).StatusCode);

        Clock.SetUtcNow(DateTimeOffset.Parse("2027-02-16T03:02:01Z"));
        await AwardDue();
        await using var db = F.NewDb();
        Assert.Equal(Me.UserId, (await db.Trophies.SingleAsync(t => t.LeagueId == league)).UserId);
    }

    [Fact]
    public async Task Ended_league_standings_keep_the_champion_after_the_award_and_a_later_leave()
    {
        var b = await NewPlayer();
        var league = await CreateLeague(Me, Feb1, Feb15);
        await Join(Me, b, league);
        await ConfirmAt(Me, Bar, InsideBar, (DrinkType.Beer, 3));
        await ConfirmAt(b, Bar, InsideBar, OneBeer);
        Clock.SetUtcNow(DateTimeOffset.Parse("2027-02-16T03:02:01Z"));
        await AwardDue();

        await F.ReauthenticateAsync(Me);
        Assert.Equal(HttpStatusCode.NoContent, (await Me.Http.PostAsync($"/leagues/{league}/leave", null)).StatusCode);

        var rows = await Standings(b, league);
        Assert.Equal(2, rows.Count);
        Assert.Equal(Me.UserId, rows[0].UserId);
        Assert.Equal(1, rows[0].Rank);
        Assert.Equal(2, rows[1].Rank);
        // The caller still has to be a current member.
        Assert.Equal(HttpStatusCode.NotFound, (await GetStandings(Me, league)).StatusCode);
    }

    [Fact]
    public async Task Member_who_left_mid_league_stays_unranked_after_the_league_ends()
    {
        var b = await NewPlayer();
        var league = await CreateLeague(Me, Feb1, Feb15);
        await Join(Me, b, league);
        await ConfirmAt(b, Bar, InsideBar, (DrinkType.Beer, 5));
        await F.ReauthenticateAsync(b);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Http.PostAsync($"/leagues/{league}/leave", null)).StatusCode);
        await ConfirmAt(Me, Bar, InsideBar, OneBeer);

        Clock.SetUtcNow(DateTimeOffset.Parse("2027-02-16T03:02:01Z"));
        await AwardDue();
        Assert.Equal(Me.UserId, Assert.Single(await Standings(Me, league)).UserId);
        await using var db = F.NewDb();
        Assert.Equal(Me.UserId, (await db.Trophies.SingleAsync(t => t.LeagueId == league)).UserId);
    }

    [Fact]
    public async Task My_trophies_lists_newest_first()
    {
        var l1 = await CreateLeague(Me, Feb1, Feb15, "Primeira");
        var l2 = await CreateLeague(Me, Feb1, Feb28, "Segunda");
        await Confirm(InsideBar, (DrinkType.Beer, 1));

        Clock.SetUtcNow(DateTimeOffset.Parse("2027-02-16T03:02:01Z"));
        await AwardDue();
        Clock.SetUtcNow(DateTimeOffset.Parse("2027-03-01T03:02:01Z"));
        await AwardDue();

        await F.ReauthenticateAsync(Me);
        var res = await Me.Http.GetAsync("/me/trophies");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var items = (await res.Content.ReadFromJsonAsync<JsonElement[]>())!;
        Assert.Equal(["Segunda", "Primeira"], items.Select(i => i.GetProperty("leagueName").GetString()));
        Assert.Equal(l2, items[0].GetProperty("leagueId").GetGuid());
        Assert.Equal(l1, items[1].GetProperty("leagueId").GetGuid());
        Assert.True(items[0].GetProperty("awardedAt").GetDateTimeOffset() > items[1].GetProperty("awardedAt").GetDateTimeOffset());
    }
}
