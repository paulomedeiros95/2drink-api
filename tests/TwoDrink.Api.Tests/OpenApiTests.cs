using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TwoDrink.Api.Tests.Infrastructure;

namespace TwoDrink.Api.Tests;

public class OpenApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Development_serves_the_openapi_document_anonymously()
    {
        using var dev = factory.InEnvironment("Development");
        var res = await dev.CreateClient().GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var doc = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2DRINK API", doc.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("v1", doc.GetProperty("info").GetProperty("version").GetString());

        var bearer = doc.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());

        var paths = doc.GetProperty("paths");
        Assert.True(paths.GetProperty("/me").GetProperty("get").TryGetProperty("security", out _));
        Assert.Equal("Venues", paths.GetProperty("/venues/nearby").GetProperty("get").GetProperty("tags")[0].GetString());
        Assert.True(paths.GetProperty("/venues/nearby").GetProperty("get").GetProperty("responses").TryGetProperty("503", out _));
        Assert.False(paths.GetProperty("/health").GetProperty("get").TryGetProperty("security", out _));
        Assert.False(paths.GetProperty("/auth/google").GetProperty("post").TryGetProperty("security", out _));
    }

    [Fact]
    public async Task Development_serves_swagger_ui_anonymously()
    {
        using var dev = factory.InEnvironment("Development");
        var res = await dev.CreateClient().GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Production")]
    public async Task Other_environments_expose_neither_openapi_nor_swagger(string environment)
    {
        using var other = factory.InEnvironment(environment);
        var client = await other.CreateSignedInClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await other.CreateClient().GetAsync("/openapi/v1.json")).StatusCode);
    }
}
