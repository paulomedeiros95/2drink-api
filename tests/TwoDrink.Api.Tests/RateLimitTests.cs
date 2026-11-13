using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using TwoDrink.Api.Tests.Infrastructure;

namespace TwoDrink.Api.Tests;

public class RateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    const int Limit = 5;

    [Fact]
    public async Task Health_is_not_rate_limited_but_other_endpoints_are()
    {
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:PermitLimit", Limit.ToString()));
        var http = limited.CreateClient();

        for (var i = 0; i < Limit * 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);

        // Same (anonymous) partition: the limiter is on for everything else.
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < Limit + 1; i++)
            statuses.Add((await http.PostAsJsonAsync("/auth/refresh", new { refreshToken = "" })).StatusCode);
        Assert.All(statuses.Take(Limit), s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task Forwarded_for_from_a_trusted_proxy_sets_the_client_partition()
    {
        // With forwarded headers on, the client IP from X-Forwarded-For becomes the partition, so two forwarded clients
        // behind the same proxy get separate buckets (without it they would share one and the third call would be 429).
        using var limited = factory.WithWebHostBuilder(b => b
            .UseSetting("RateLimiting:PermitLimit", "1")
            .UseSetting("ForwardedHeaders:Enabled", "true")
            .UseSetting("ForwardedHeaders:KnownNetworks:0", "0.0.0.0/0"));
        var http = limited.CreateClient();

        async Task<HttpStatusCode> Refresh(string clientIp)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh") { Content = JsonContent.Create(new { refreshToken = "" }) };
            req.Headers.Add("X-Forwarded-For", clientIp);
            return (await http.SendAsync(req)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await Refresh("203.0.113.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Refresh("203.0.113.1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Refresh("203.0.113.2"));
    }
}
