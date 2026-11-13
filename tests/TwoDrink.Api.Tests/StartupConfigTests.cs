using System.Net;
using Microsoft.AspNetCore.Hosting;
using TwoDrink.Api.Tests.Infrastructure;

namespace TwoDrink.Api.Tests;

/// <summary>Production misconfiguration must stop the host at startup, not surface on the first request.</summary>
public class StartupConfigTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    void AssertFailsToStart(Action<IWebHostBuilder> configure, string expectedKey)
    {
        using var broken = factory.WithWebHostBuilder(configure);
        var ex = Assert.ThrowsAny<Exception>(() => broken.CreateClient());
        Assert.Contains(expectedKey, ex.ToString());
    }

    [Fact]
    public void Missing_signing_key_stops_the_host() =>
        AssertFailsToStart(b => b.UseSetting("Jwt:SigningKey", ""), "Jwt:SigningKey");

    [Fact]
    public void Short_signing_key_stops_the_host() =>
        AssertFailsToStart(b => b.UseSetting("Jwt:SigningKey", "too-short"), "Jwt:SigningKey");

    [Fact]
    public void Missing_issuer_stops_the_host() =>
        AssertFailsToStart(b => b.UseSetting("Jwt:Issuer", ""), "Jwt:Issuer");

    [Fact]
    public void Missing_connection_string_stops_the_host() =>
        AssertFailsToStart(b => b.UseSetting("ConnectionStrings:Default", ""), "ConnectionStrings:Default");

    [Fact]
    public void Outside_development_an_external_audience_is_required() =>
        AssertFailsToStart(b => b
            .UseSetting("Auth:Google:ClientIds:0", "")
            .UseSetting("Auth:Apple:Audiences:0", ""), "Auth:Google:ClientIds");

    [Fact]
    public async Task Development_does_not_require_external_audiences()
    {
        using var dev = factory.WithWebHostBuilder(b => b
            .UseEnvironment("Development")
            .UseSetting("Auth:Google:ClientIds:0", "")
            .UseSetting("Auth:Apple:Audiences:0", ""));
        Assert.Equal(HttpStatusCode.OK, (await dev.CreateClient().GetAsync("/health")).StatusCode);
    }
}
