using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TwoDrink.Api.Tests.Infrastructure;

namespace TwoDrink.Api.Tests;

public class LeagueRenewTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    // First instant of 2027-02-11 in Sao Paulo (UTC-3): the Feb 1-10 league below has ended.
    static readonly DateTimeOffset Feb11Start = new(2027, 2, 11, 3, 0, 0, TimeSpan.Zero);

    PlayerClient A = null!;

    public async Task InitializeAsync()
    {
        factory.Clock.SetUtcNow(TestClock.DefaultStart);
        A = await NewPlayer();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    Task<PlayerClient> NewPlayer() => factory.CreatePlayerClientAsync("rn-" + Guid.NewGuid().ToString("N"));

    static Task<HttpResponseMessage> PostLeague(PlayerClient p, DateOnly start, DateOnly end) =>
        p.Http.PostAsJsonAsync("/leagues", new { name = "Liga", description = "desc", startDate = start, endDate = end });

    static async Task<Guid> CreateLeague(PlayerClient p, DateOnly start, DateOnly end)
    {
        var res = await PostLeague(p, start, end);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    static async Task Join(PlayerClient creator, PlayerClient member, Guid leagueId)
    {
        var inv = await creator.Http.PostAsync($"/leagues/{leagueId}/invites", null);
        var token = (await inv.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var res = await member.Http.PostAsync($"/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    static Task<HttpResponseMessage> Renew(PlayerClient p, Guid leagueId, string? name = null,
        DateOnly? start = null, DateOnly? end = null) =>
        p.Http.PostAsJsonAsync($"/leagues/{leagueId}/renew", new
        {
            name,
            startDate = start ?? new DateOnly(2027, 2, 11),
            endDate = end ?? new DateOnly(2027, 2, 28),
        });

    static async Task AssertCode(HttpStatusCode status, string code, HttpResponseMessage r)
    {
        Assert.Equal(status, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    async Task GoToFeb11(params PlayerClient[] players)
    {
        factory.Clock.SetUtcNow(Feb11Start);
        foreach (var p in players) await factory.ReauthenticateAsync(p);
    }

    static async Task<JsonElement[]> MyLeagues(PlayerClient p) =>
        (await p.Http.GetFromJsonAsync<JsonElement[]>("/me/leagues"))!;

    static Guid[] Ids(JsonElement skipped) => skipped.EnumerateArray().Select(e => e.GetGuid()).ToArray();

    [Fact]
    public async Task Renew_copies_active_members()
    {
        var b = await NewPlayer();
        var old = await CreateLeague(A, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 10));
        await Join(A, b, old);

        await using (var db = factory.NewDb())
        {
            (await db.Leagues.SingleAsync(l => l.Id == old)).CoverBlobName = "cover-1.jpg";
            await db.SaveChangesAsync();
        }

        await GoToFeb11(A, b);
        var res = await Renew(A, old);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var newId = body.GetProperty("id").GetGuid();
        Assert.NotEqual(old, newId);
        Assert.Equal("Liga", body.GetProperty("name").GetString());
        Assert.Equal("desc", body.GetProperty("description").GetString());
        Assert.Equal("active", body.GetProperty("status").GetString());
        Assert.Empty(Ids(body.GetProperty("skippedUserIds")));

        Assert.Contains(await MyLeagues(A), l => l.GetProperty("id").GetGuid() == newId && l.GetProperty("memberCount").GetInt32() == 2);
        Assert.Contains(await MyLeagues(b), l => l.GetProperty("id").GetGuid() == newId);

        await using var db2 = factory.NewDb();
        var created = await db2.Leagues.AsNoTracking().SingleAsync(l => l.Id == newId);
        Assert.Equal("cover-1.jpg", created.CoverBlobName);
        Assert.Null(created.TrophiesAwardedAt);
        Assert.Equal(A.UserId, created.CreatorId);
        var oldLeague = await db2.Leagues.AsNoTracking().SingleAsync(l => l.Id == old);
        Assert.Equal(new DateOnly(2027, 2, 10), oldLeague.EndDate);
        Assert.Equal(2, await db2.LeagueMemberships.CountAsync(m => m.LeagueId == old && m.LeftAt == null));
    }

    [Fact]
    public async Task Renew_uses_given_name()
    {
        var old = await CreateLeague(A, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 10));
        await GoToFeb11(A);
        var res = await Renew(A, old, name: "  Temporada 2  ");
        Assert.Equal("Temporada 2", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Renew_skips_members_without_slot()
    {
        var b = await NewPlayer();
        var old = await CreateLeague(A, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 10));
        await Join(A, b, old);
        await GoToFeb11(A, b);
        // B fills both slots with new leagues.
        await CreateLeague(b, new DateOnly(2027, 2, 11), new DateOnly(2027, 2, 20));
        await CreateLeague(b, new DateOnly(2027, 2, 11), new DateOnly(2027, 2, 20));

        var res = await Renew(A, old);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var newId = body.GetProperty("id").GetGuid();
        Assert.Equal(new[] { b.UserId }, Ids(body.GetProperty("skippedUserIds")));
        Assert.DoesNotContain(await MyLeagues(b), l => l.GetProperty("id").GetGuid() == newId);
        Assert.Contains(await MyLeagues(A), l => l.GetProperty("id").GetGuid() == newId && l.GetProperty("memberCount").GetInt32() == 1);
    }

    [Fact]
    public async Task Renew_fails_when_creator_has_no_slot()
    {
        var b = await NewPlayer();
        var old = await CreateLeague(A, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 10));
        await Join(A, b, old);
        await GoToFeb11(A, b);
        await CreateLeague(A, new DateOnly(2027, 2, 11), new DateOnly(2027, 2, 20));
        await CreateLeague(A, new DateOnly(2027, 2, 11), new DateOnly(2027, 2, 20));

        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_limit", await Renew(A, old));

        Assert.Single(await MyLeagues(b)); // only the old (ended) league
        Assert.Equal(3, (await MyLeagues(A)).Length); // old + the two created, no renewed one
        await using var db = factory.NewDb();
        Assert.Equal(3, await db.Leagues.CountAsync(l => l.CreatorId == A.UserId));
    }

    [Fact]
    public async Task Renew_does_not_carry_over_members_who_left()
    {
        var b = await NewPlayer();
        var old = await CreateLeague(A, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 10));
        await Join(A, b, old);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Http.PostAsync($"/leagues/{old}/leave", null)).StatusCode);
        await GoToFeb11(A, b);

        var body = await (await Renew(A, old)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(Ids(body.GetProperty("skippedUserIds")));
        Assert.Empty(await MyLeagues(b));
        var mine = (await MyLeagues(A)).Single(l => l.GetProperty("id").GetGuid() == body.GetProperty("id").GetGuid());
        Assert.Equal(1, mine.GetProperty("memberCount").GetInt32());
    }

    [Fact]
    public async Task Renew_requires_ended_league()
    {
        var old = await CreateLeague(A, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 10));
        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_not_ended", await Renew(A, old));

        // Last minute of the last day: still not ended.
        factory.Clock.SetUtcNow(Feb11Start.AddMinutes(-1));
        await factory.ReauthenticateAsync(A);
        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_not_ended", await Renew(A, old));
    }

    [Fact]
    public async Task Only_creator_renews()
    {
        var b = await NewPlayer();
        var old = await CreateLeague(A, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 10));
        await Join(A, b, old);
        await GoToFeb11(A, b);
        await AssertCode(HttpStatusCode.Forbidden, "not_league_creator", await Renew(b, old));
        await AssertCode(HttpStatusCode.NotFound, "league_not_found", await Renew(A, Guid.NewGuid()));
    }

    [Fact]
    public async Task Renew_reuses_create_validation_codes()
    {
        var old = await CreateLeague(A, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 10));
        await GoToFeb11(A);
        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_name_invalid", await Renew(A, old, name: new string('x', 61)));
        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_dates_invalid", await Renew(A, old, start: new DateOnly(2027, 2, 20), end: new DateOnly(2027, 2, 15)));
        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_dates_invalid", await Renew(A, old, start: new DateOnly(2027, 2, 1), end: new DateOnly(2027, 2, 5)));
        await AssertCode(HttpStatusCode.UnprocessableEntity, "dates_outside_season", await Renew(A, old, start: new DateOnly(2027, 6, 20), end: new DateOnly(2027, 7, 10)));
    }
}
