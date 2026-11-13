using System.Net;
using Microsoft.AspNetCore.Hosting;
using TwoDrink.Api.Tests.Infrastructure;

namespace TwoDrink.Api.Tests;

public class HealthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_returns_ok()
    {
        var res = await factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Health_returns_503_when_database_is_unreachable()
    {
        using var broken = factory.WithWebHostBuilder(b => b.UseSetting(
            "ConnectionStrings:Default",
            "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2;Pooling=false"));
        var res = await broken.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }
}
