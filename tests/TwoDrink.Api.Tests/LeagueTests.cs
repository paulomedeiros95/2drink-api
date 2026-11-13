using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TwoDrink.Api.Tests.Infrastructure;

namespace TwoDrink.Api.Tests;

public class LeagueTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    // Default clock: 2027-02-06T23:00Z = 2027-02-06 20:00 in Sao Paulo (season 2027 H1).
    PlayerClient A = null!;

    public async Task InitializeAsync()
    {
        factory.Clock.SetUtcNow(TestClock.DefaultStart);
        A = await NewPlayer();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    Task<PlayerClient> NewPlayer() => factory.CreatePlayerClientAsync("lg-" + Guid.NewGuid().ToString("N"));

    static Task<HttpResponseMessage> PostLeague(PlayerClient p, string? name = "Liga", string? description = null,
        DateOnly? start = null, DateOnly? end = null) =>
        p.Http.PostAsJsonAsync("/leagues", new
        {
            name,
            description,
            startDate = start ?? new DateOnly(2027, 2, 1),
            endDate = end ?? new DateOnly(2027, 2, 28),
        });

    static async Task<Guid> CreateLeague(PlayerClient p, DateOnly? start = null, DateOnly? end = null)
    {
        var res = await PostLeague(p, start: start, end: end);
        Assert.True(res.IsSuccessStatusCode, $"create returned {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    static async Task<string?> ProblemCode(HttpResponseMessage r)
    {
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var c) ? c.GetString() : null;
    }

    static async Task AssertCode(HttpStatusCode status, string code, HttpResponseMessage r)
    {
        Assert.Equal(status, r.StatusCode);
        Assert.Equal(code, await ProblemCode(r));
    }

    static async Task<string> NewInvite(PlayerClient creator, Guid leagueId)
    {
        var res = await creator.Http.PostAsync($"/leagues/{leagueId}/invites", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    static Task<HttpResponseMessage> Accept(PlayerClient p, string token) =>
        p.Http.PostAsync($"/invites/{token}/accept", null);

    static async Task<JsonElement[]> MyLeagues(PlayerClient p)
    {
        var res = await p.Http.GetAsync("/me/leagues");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement[]>())!;
    }

    // First instant of 2027-02-11 in Sao Paulo (UTC-3).
    static readonly DateTimeOffset Feb11Start = new(2027, 2, 11, 3, 0, 0, TimeSpan.Zero);

    // ---- create ----

    [Fact]
    public async Task Create_league_returns_summary_and_makes_creator_member()
    {
        var res = await PostLeague(A, "  Turma  ", "desc");
        Assert.True(res.IsSuccessStatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Turma", body.GetProperty("name").GetString());
        Assert.Equal("desc", body.GetProperty("description").GetString());
        Assert.Equal("2027-02-01", body.GetProperty("startDate").GetString());
        Assert.Equal("2027-02-28", body.GetProperty("endDate").GetString());
        Assert.Equal("active", body.GetProperty("status").GetString());

        var l = Assert.Single(await MyLeagues(A));
        Assert.Equal(1, l.GetProperty("memberCount").GetInt32());

        await using var db = factory.NewDb();
        var league = await db.Leagues.AsNoTracking().SingleAsync(x => x.Id == body.GetProperty("id").GetGuid());
        Assert.Equal(A.UserId, league.CreatorId);
        Assert.Null(league.CoverBlobName);
    }

    [Fact]
    public async Task Create_league_outside_season_rejected()
    {
        // Starts in 2027 H1, ends in July (no season there).
        await AssertCode(HttpStatusCode.UnprocessableEntity, "dates_outside_season",
            await PostLeague(A, start: new DateOnly(2027, 6, 1), end: new DateOnly(2027, 7, 15)));
        // Spans two seasons (2026 H2 / 2027 H1): not within ONE season.
        await AssertCode(HttpStatusCode.UnprocessableEntity, "dates_outside_season",
            await PostLeague(A, start: new DateOnly(2026, 12, 20), end: new DateOnly(2027, 3, 1)));
    }

    [Fact]
    public async Task Create_league_validation_codes()
    {
        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_name_invalid", await PostLeague(A, name: "   "));
        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_name_invalid", await PostLeague(A, name: null));
        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_name_invalid", await PostLeague(A, name: new string('x', 61)));
        Assert.True((await PostLeague(A, name: new string('x', 60), start: new DateOnly(2027, 3, 1), end: new DateOnly(2027, 3, 2))).IsSuccessStatusCode);
        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_description_invalid",
            await PostLeague(A, description: new string('d', 281)));
        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_dates_invalid",
            await PostLeague(A, start: new DateOnly(2027, 2, 20), end: new DateOnly(2027, 2, 10)));
        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_dates_invalid",
            await PostLeague(A, start: new DateOnly(2027, 2, 1), end: new DateOnly(2027, 2, 5)));
    }

    [Fact]
    public async Task Third_active_league_rejected()
    {
        await CreateLeague(A);
        await CreateLeague(A);
        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_limit", await PostLeague(A));
    }

    [Fact]
    public async Task Upcoming_leagues_count_toward_the_limit()
    {
        await CreateLeague(A, new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 10));
        await CreateLeague(A, new DateOnly(2027, 4, 1), new DateOnly(2027, 4, 10));
        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_limit", await PostLeague(A));
    }

    [Fact]
    public async Task Ended_league_frees_slot()
    {
        await CreateLeague(A, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 10));
        await CreateLeague(A, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 10));
        // One minute before the end (Feb 11 00:00 in Sao Paulo) the slots are still taken.
        factory.Clock.SetUtcNow(Feb11Start.AddMinutes(-1));
        await factory.ReauthenticateAsync(A);
        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_limit",
            await PostLeague(A, start: new DateOnly(2027, 2, 10), end: new DateOnly(2027, 2, 28)));

        factory.Clock.SetUtcNow(Feb11Start);
        await factory.ReauthenticateAsync(A);
        var res = await PostLeague(A, start: new DateOnly(2027, 2, 11), end: new DateOnly(2027, 2, 28));
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Concurrent_creates_with_one_free_slot_only_one_wins()
    {
        await CreateLeague(A);
        var rs = await Task.WhenAll(PostLeague(A, "a"), PostLeague(A, "b"));
        Assert.Single(rs, r => r.IsSuccessStatusCode);
        var loser = Assert.Single(rs, r => !r.IsSuccessStatusCode);
        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_limit", loser);
    }

    // ---- invites ----

    [Fact]
    public async Task Only_creator_generates_invites()
    {
        var league = await CreateLeague(A);
        var b = await NewPlayer();
        var outsider = await NewPlayer();
        Assert.Equal(HttpStatusCode.OK, (await Accept(b, await NewInvite(A, league))).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await b.Http.PostAsync($"/leagues/{league}/invites", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.Http.PostAsync($"/leagues/{league}/invites", null)).StatusCode);
        await AssertCode(HttpStatusCode.NotFound, "league_not_found",
            await A.Http.PostAsync($"/leagues/{Guid.NewGuid()}/invites", null));
    }

    [Fact]
    public async Task Invite_response_has_url_expiry_and_stores_only_the_hash()
    {
        var league = await CreateLeague(A);
        var res = await A.Http.PostAsync($"/leagues/{league}/invites", null);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;
        Assert.Equal($"https://2drink.app/i/{token}", body.GetProperty("url").GetString());
        Assert.Equal(factory.Clock.GetUtcNow().AddDays(7), body.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(43, token.Length); // 32 bytes, base64url, no padding
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        await using var db = factory.NewDb();
        var invite = await db.LeagueInvites.AsNoTracking().SingleAsync(i => i.LeagueId == league);
        Assert.Equal(hash, invite.TokenHash);
    }

    [Fact]
    public async Task Accept_returns_summary_and_adds_member()
    {
        var league = await CreateLeague(A);
        var b = await NewPlayer();
        var res = await Accept(b, await NewInvite(A, league));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(league, body.GetProperty("id").GetGuid());
        Assert.Equal(2, Assert.Single(await MyLeagues(b)).GetProperty("memberCount").GetInt32());
    }

    [Fact]
    public async Task Unknown_invite_token_rejected()
    {
        var b = await NewPlayer();
        await AssertCode(HttpStatusCode.NotFound, "invite_invalid", await Accept(b, "nope"));
    }

    [Fact]
    public async Task Invite_is_single_use()
    {
        var league = await CreateLeague(A);
        var token = await NewInvite(A, league);
        var b = await NewPlayer();
        var c = await NewPlayer();
        Assert.Equal(HttpStatusCode.OK, (await Accept(b, token)).StatusCode);
        await AssertCode(HttpStatusCode.UnprocessableEntity, "invite_used", await Accept(c, token));
    }

    [Fact]
    public async Task Invite_expires_after_7_days()
    {
        var league = await CreateLeague(A, new DateOnly(2027, 2, 1), new DateOnly(2027, 6, 30));
        var token = await NewInvite(A, league);
        var b = await NewPlayer();
        factory.Clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1));
        await factory.ReauthenticateAsync(b);
        await AssertCode(HttpStatusCode.UnprocessableEntity, "invite_expired", await Accept(b, token));
    }

    [Fact]
    public async Task Concurrent_accepts_only_one_wins()
    {
        var league = await CreateLeague(A);
        var t = await NewInvite(A, league);
        var b = await NewPlayer();
        var c = await NewPlayer();
        var rs = await Task.WhenAll(Accept(b, t), Accept(c, t));
        Assert.Single(rs, r => r.IsSuccessStatusCode);
        var loser = Assert.Single(rs, r => !r.IsSuccessStatusCode);
        await AssertCode(HttpStatusCode.UnprocessableEntity, "invite_used", loser);
    }

    [Fact]
    public async Task Accept_blocked_when_user_has_two_leagues_and_invite_stays_usable()
    {
        var league = await CreateLeague(A);
        var token = await NewInvite(A, league);
        var b = await NewPlayer();
        await CreateLeague(b);
        await CreateLeague(b);

        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_limit", await Accept(b, token));

        // The failed accept rolled the claim back.
        await using (var db = factory.NewDb())
            Assert.Null((await db.LeagueInvites.AsNoTracking().SingleAsync(i => i.LeagueId == league)).UsedBy);
        var c = await NewPlayer();
        Assert.Equal(HttpStatusCode.OK, (await Accept(c, token)).StatusCode);
    }

    [Fact]
    public async Task Accept_into_ended_league_rejected()
    {
        var league = await CreateLeague(A, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 10));
        var token = await NewInvite(A, league);
        var b = await NewPlayer();
        factory.Clock.SetUtcNow(Feb11Start);
        await factory.ReauthenticateAsync(b);
        await AssertCode(HttpStatusCode.UnprocessableEntity, "league_ended", await Accept(b, token));
    }

    [Fact]
    public async Task Accept_when_already_member_rejected()
    {
        var league = await CreateLeague(A);
        var token = await NewInvite(A, league);
        await AssertCode(HttpStatusCode.UnprocessableEntity, "already_member", await Accept(A, token));
    }

    // ---- leave ----

    [Fact]
    public async Task Leaving_is_permanent()
    {
        var league = await CreateLeague(A);
        var b = await NewPlayer();
        Assert.Equal(HttpStatusCode.OK, (await Accept(b, await NewInvite(A, league))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Http.PostAsync($"/leagues/{league}/leave", null)).StatusCode);

        await AssertCode(HttpStatusCode.UnprocessableEntity, "left_league", await Accept(b, await NewInvite(A, league)));
        Assert.Empty(await MyLeagues(b));
    }

    [Fact]
    public async Task Leave_when_not_member_is_404()
    {
        var league = await CreateLeague(A);
        var b = await NewPlayer();
        await AssertCode(HttpStatusCode.NotFound, "not_member", await b.Http.PostAsync($"/leagues/{league}/leave", null));
        await AssertCode(HttpStatusCode.NotFound, "not_member", await b.Http.PostAsync($"/leagues/{Guid.NewGuid()}/leave", null));
        // Leaving twice: the second time the person is no longer a member.
        var c = await NewPlayer();
        await Accept(c, await NewInvite(A, league));
        Assert.True((await c.Http.PostAsync($"/leagues/{league}/leave", null)).IsSuccessStatusCode);
        await AssertCode(HttpStatusCode.NotFound, "not_member", await c.Http.PostAsync($"/leagues/{league}/leave", null));
    }

    [Fact]
    public async Task Creator_cannot_leave_active_league_but_can_after_it_ends()
    {
        var league = await CreateLeague(A, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 10));
        await AssertCode(HttpStatusCode.UnprocessableEntity, "creator_cannot_leave", await A.Http.PostAsync($"/leagues/{league}/leave", null));

        factory.Clock.SetUtcNow(Feb11Start);
        await factory.ReauthenticateAsync(A);
        Assert.True((await A.Http.PostAsync($"/leagues/{league}/leave", null)).IsSuccessStatusCode);
    }

    // ---- GET /me/leagues ----

    [Fact]
    public async Task My_leagues_lists_status_member_count_and_orders_newest_first()
    {
        var upcoming = await CreateLeague(A, new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 10));
        var active = await CreateLeague(A, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 28));
        var b = await NewPlayer();
        await Accept(b, await NewInvite(A, active));
        var left = await NewPlayer();
        await Accept(left, await NewInvite(A, active));
        await left.Http.PostAsync($"/leagues/{active}/leave", null);

        var mine = await MyLeagues(A);
        Assert.Equal([upcoming, active], mine.Select(l => l.GetProperty("id").GetGuid()));
        Assert.Equal("upcoming", mine[0].GetProperty("status").GetString());
        Assert.Equal("active", mine[1].GetProperty("status").GetString());
        Assert.Equal(1, mine[0].GetProperty("memberCount").GetInt32());
        Assert.Equal(2, mine[1].GetProperty("memberCount").GetInt32());
    }

    [Fact]
    public async Task My_leagues_includes_ended_leagues_from_the_day_after_the_end()
    {
        var id = await CreateLeague(A, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 6));
        Assert.Equal("active", Assert.Single(await MyLeagues(A)).GetProperty("status").GetString());

        // 2027-02-07 00:00 in Sao Paulo.
        factory.Clock.SetUtcNow(new DateTimeOffset(2027, 2, 7, 3, 0, 0, TimeSpan.Zero));
        await factory.ReauthenticateAsync(A);
        var l = Assert.Single(await MyLeagues(A));
        Assert.Equal(id, l.GetProperty("id").GetGuid());
        Assert.Equal("ended", l.GetProperty("status").GetString());
    }

    [Fact]
    public async Task League_endpoints_require_a_player()
    {
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/me/leagues")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync("/invites/x/accept", null)).StatusCode);
    }
}
