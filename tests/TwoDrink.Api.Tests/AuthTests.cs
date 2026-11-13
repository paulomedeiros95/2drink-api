using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TwoDrink.Api.Tests.Infrastructure;

namespace TwoDrink.Api.Tests;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    record AuthResponse(string AccessToken, string RefreshToken, bool ProfileComplete);

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    async Task<AuthResponse> Login(string subject, string provider = "google")
    {
        var res = provider == "google"
            ? await factory.CreateClient().PostAsJsonAsync("/auth/google", new { idToken = "valid:" + subject })
            : await factory.CreateClient().PostAsJsonAsync("/auth/apple", new { identityToken = "valid:" + subject });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
    }

    HttpClient Authed(AuthResponse auth)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return http;
    }

    static Task<HttpResponseMessage> PutProfile(HttpClient http, DateOnly? birthDate, string? name = "Ana",
        string? terms = "v1", string? locale = "pt-BR") =>
        http.PutAsJsonAsync("/me/profile", new { displayName = name, birthDate, acceptedTermsVersion = terms, locale });

    static async Task AssertProblem(HttpResponseMessage res, int status, string code)
    {
        Assert.Equal(status, (int)res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    DateOnly Today() => TwoDrink.Domain.Rules.BusinessTime.Today(factory.Clock.GetUtcNow());

    /// <summary>Advance to the next 02:00 UTC, which is still the previous day (23:00) in Sao Paulo.</summary>
    void AdvanceToLateEveningInSaoPaulo()
    {
        var now = factory.Clock.GetUtcNow();
        var target = new DateTimeOffset(now.Year, now.Month, now.Day, 2, 0, 0, TimeSpan.Zero);
        if (target <= now) target = target.AddDays(1);
        factory.Clock.Advance(target - now);
    }

    static string Sub(AuthResponse a) => new JwtSecurityTokenHandler().ReadJwtToken(a.AccessToken).Subject;

    [Fact]
    public async Task First_login_creates_user_with_incomplete_profile()
    {
        var r = await Login("g-123");
        Assert.False(r.ProfileComplete);
        await using var db = factory.NewDb();
        var user = db.Users.Single(u => u.Subject == "g-123");
        Assert.Equal(user.Id.ToString(), Sub(r));
        Assert.True(db.LedgerAccounts.Any(a => a.Id == user.LedgerAccountId));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(r.AccessToken);
        Assert.Equal(user.LedgerAccountId.ToString(), jwt.Claims.Single(c => c.Type == "ledger").Value);
        Assert.Equal(TimeSpan.FromHours(1), jwt.ValidTo - jwt.ValidFrom);
    }

    [Fact]
    public async Task Same_subject_logs_into_same_user()
    {
        var a = await Login("g-same");
        var b = await Login("g-same");
        Assert.Equal(Sub(a), Sub(b));
    }

    [Fact]
    public async Task Same_subject_on_different_providers_are_different_users()
    {
        var g = await Login("shared-sub");
        var ap = await Login("shared-sub", "apple");
        Assert.NotEqual(Sub(g), Sub(ap));
    }

    [Fact]
    public async Task Underage_profile_is_rejected()
    {
        var http = Authed(await Login("g-young"));
        var res = await PutProfile(http, Today().AddYears(-18).AddDays(1)); // turns 18 only tomorrow
        await AssertProblem(res, 422, "underage");
    }

    [Fact]
    public async Task Eighteenth_birthday_today_is_accepted()
    {
        var auth = await Login("g-18");
        var res = await PutProfile(Authed(auth), Today().AddYears(-18));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var again = await Login("g-18");
        Assert.True(again.ProfileComplete);
    }

    [Fact]
    public async Task Birthday_uses_sao_paulo_date_not_utc()
    {
        AdvanceToLateEveningInSaoPaulo(); // UTC date is already tomorrow; Sao Paulo date is not
        var http = Authed(await Login("g-tz"));
        var utcToday = DateOnly.FromDateTime(factory.Clock.GetUtcNow().UtcDateTime);
        Assert.NotEqual(utcToday, Today());
        await AssertProblem(await PutProfile(http, utcToday.AddYears(-18)), 422, "underage");
    }

    [Theory]
    [InlineData("future", "invalid_birth_date")]
    [InlineData("ancient", "invalid_birth_date")]
    [InlineData("missing", "invalid_birth_date")]
    [InlineData("name_empty", "display_name_invalid")]
    [InlineData("name_blank", "display_name_invalid")]
    [InlineData("name_long", "display_name_invalid")]
    [InlineData("terms_blank", "terms_required")]
    [InlineData("terms_null", "terms_required")]
    [InlineData("terms_long", "terms_required")]
    [InlineData("locale_bad", "locale_invalid")]
    public async Task Invalid_profile_is_422_with_code(string scenario, string code)
    {
        var http = Authed(await Login("g-val-" + scenario));
        var adult = new DateOnly(1990, 1, 1);
        var res = scenario switch
        {
            "future" => await PutProfile(http, Today().AddYears(1)),
            "ancient" => await PutProfile(http, new DateOnly(1899, 12, 31)),
            "missing" => await PutProfile(http, null),
            "name_empty" => await PutProfile(http, adult, name: ""),
            "name_blank" => await PutProfile(http, adult, name: "   "),
            "name_long" => await PutProfile(http, adult, name: new string('x', 41)),
            "terms_blank" => await PutProfile(http, adult, terms: " "),
            "terms_null" => await PutProfile(http, adult, terms: null),
            "terms_long" => await PutProfile(http, adult, terms: new string('v', 33)),
            _ => await PutProfile(http, adult, locale: "fr"),
        };
        await AssertProblem(res, 422, code);
    }

    [Fact]
    public async Task Profile_accepts_english_locale_and_trims_name()
    {
        var auth = await Login("g-en");
        var http = Authed(auth);
        Assert.Equal(HttpStatusCode.OK, (await PutProfile(http, new DateOnly(1990, 1, 1), name: "  Bia  ", locale: "en")).StatusCode);
        var me = await http.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal("Bia", me.GetProperty("displayName").GetString());
        Assert.Equal("en", me.GetProperty("locale").GetString());
        Assert.Equal(Sub(auth), me.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Profile_requires_authentication()
    {
        var res = await PutProfile(factory.CreateClient(), new DateOnly(1990, 1, 1));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Player_endpoints_require_complete_profile()
    {
        var http = Authed(await Login("g-incomplete"));
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/me")).StatusCode);

        await PutProfile(http, new DateOnly(1990, 1, 1));
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/me")).StatusCode);
    }

    [Theory]
    [InlineData("/auth/google", "idToken")]
    [InlineData("/auth/apple", "identityToken")]
    public async Task Invalid_token_is_401(string path, string field)
    {
        var res = await factory.CreateClient().PostAsJsonAsync(path, new Dictionary<string, string> { [field] = "bad" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Refresh_token_is_single_use()
    {
        var first = await Login("g-refresh");
        var http = factory.CreateClient();
        var ok = await http.PostAsJsonAsync("/auth/refresh", new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var second = (await ok.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.Equal(Sub(first), Sub(second));

        var reuse = await http.PostAsJsonAsync("/auth/refresh", new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        // the rotated-in token still works
        var next = await http.PostAsJsonAsync("/auth/refresh", new { refreshToken = second.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [Fact]
    public async Task Refresh_token_expires_after_60_days_and_unknown_is_401()
    {
        var auth = await Login("g-expire");
        var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.PostAsJsonAsync("/auth/refresh", new { refreshToken = "nope" })).StatusCode);
        factory.Clock.Advance(TimeSpan.FromDays(61));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.PostAsJsonAsync("/auth/refresh", new { refreshToken = auth.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task Refresh_tokens_are_stored_hashed()
    {
        var auth = await Login("g-hash");
        await using var db = factory.NewDb();
        var userId = Guid.Parse(Sub(auth));
        var stored = db.RefreshTokens.Where(t => t.UserId == userId).Select(t => t.TokenHash).ToList();
        Assert.NotEmpty(stored);
        Assert.DoesNotContain(auth.RefreshToken, stored);
    }

    [Fact]
    public async Task Access_token_expires_after_one_hour()
    {
        var auth = await Login("g-exp");
        await PutProfile(Authed(auth), new DateOnly(1990, 1, 1));
        var http = Authed(auth);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/me")).StatusCode);
        factory.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/me")).StatusCode);
    }

    [Fact]
    public async Task Create_player_client_helper_returns_usable_player()
    {
        var p = await factory.CreatePlayerClientAsync("helper");
        Assert.Equal(HttpStatusCode.OK, (await p.Http.GetAsync("/me")).StatusCode);
        Assert.NotEqual(Guid.Empty, p.LedgerAccountId);
    }
}
