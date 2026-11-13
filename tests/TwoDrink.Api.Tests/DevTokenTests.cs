using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TwoDrink.Api.Tests.Infrastructure;

namespace TwoDrink.Api.Tests;

public class DevTokenTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Development_with_flag_issues_a_token_that_works_on_me()
    {
        using var dev = factory.InEnvironment("Development", devAuth: true);
        var http = dev.CreateClient();

        var res = await http.PostAsJsonAsync("/dev/token", new { subject = "alice", displayName = "Alice", completeProfile = true });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("profileComplete").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("refreshToken").GetString()));

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        var me = await http.GetAsync("/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var meBody = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Alice", meBody.GetProperty("displayName").GetString());

        await using var db = factory.NewDb();
        Assert.Single(db.Users.Where(u => u.Subject == "dev:alice"));
    }

    [Fact]
    public async Task Same_subject_returns_the_same_user_and_profile_is_optional()
    {
        using var dev = factory.InEnvironment("Development", devAuth: true);
        var http = dev.CreateClient();

        var first = await (await http.PostAsJsonAsync("/dev/token", new { subject = "bob" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(first.GetProperty("profileComplete").GetBoolean());
        var second = await (await http.PostAsJsonAsync("/dev/token", new { subject = "bob", completeProfile = true })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(second.GetProperty("profileComplete").GetBoolean());

        await using var db = factory.NewDb();
        Assert.Single(db.Users.Where(u => u.Subject == "dev:bob"));
    }

    [Fact]
    public async Task Blank_subject_is_rejected()
    {
        using var dev = factory.InEnvironment("Development", devAuth: true);
        var res = await dev.CreateClient().PostAsJsonAsync("/dev/token", new { subject = " " });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Development_without_flag_has_no_dev_route()
    {
        using var dev = factory.InEnvironment("Development", devAuth: false);
        var res = await (await dev.CreateSignedInClientAsync()).PostAsJsonAsync("/dev/token", new { subject = "x" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Outside_development_the_flag_does_not_create_the_route(string environment)
    {
        using var other = factory.InEnvironment(environment, devAuth: true);
        var res = await (await other.CreateSignedInClientAsync()).PostAsJsonAsync("/dev/token", new { subject = "x" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
