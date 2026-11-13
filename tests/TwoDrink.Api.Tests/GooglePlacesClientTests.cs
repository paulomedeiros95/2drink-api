using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TwoDrink.Domain.Presence;
using TwoDrink.Infrastructure.Places;

namespace TwoDrink.Api.Tests;

public class GooglePlacesClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static GooglePlacesClient Client(StubHandler h, string? key = "test-key")
    {
        var config = new ConfigurationBuilder();
        if (key is not null) config.AddInMemoryCollection(new Dictionary<string, string?> { ["Google:PlacesApiKey"] = key });
        return new GooglePlacesClient(new HttpClient(h), config.Build());
    }

    private const string NearbyJson = """
        { "places": [
          { "id": "p1", "displayName": { "text": "Bar do Ze", "languageCode": "pt" },
            "formattedAddress": "Rua A, 1", "location": { "latitude": -23.55, "longitude": -46.63 } },
          { "id": "p2", "displayName": { "text": "Sem Endereco" }, "location": { "latitude": -23.56, "longitude": -46.64 } }
        ] }
        """;

    [Fact]
    public async Task Nearby_sends_search_nearby_request_as_documented()
    {
        var h = new StubHandler(HttpStatusCode.OK, NearbyJson);
        var result = await Client(h).NearbyAsync(new GeoPoint(-23.5505, -46.6333), default);

        Assert.Equal(HttpMethod.Post, h.Request!.Method);
        Assert.Equal("https://places.googleapis.com/v1/places:searchNearby", h.Request.RequestUri!.ToString());
        Assert.Equal("test-key", h.Request.Headers.GetValues("X-Goog-Api-Key").Single());
        Assert.Equal("places.id,places.displayName,places.formattedAddress,places.location",
            h.Request.Headers.GetValues("X-Goog-FieldMask").Single());

        var body = JsonDocument.Parse(h.RequestBody!).RootElement;
        Assert.Equal(["bar", "restaurant"], body.GetProperty("includedTypes").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(20, body.GetProperty("maxResultCount").GetInt32());
        var circle = body.GetProperty("locationRestriction").GetProperty("circle");
        Assert.Equal(300, circle.GetProperty("radius").GetDouble());
        Assert.Equal(-23.5505, circle.GetProperty("center").GetProperty("latitude").GetDouble());
        Assert.Equal(-46.6333, circle.GetProperty("center").GetProperty("longitude").GetDouble());

        Assert.Equal(2, result.Count);
        Assert.Equal(new PlaceResult("p1", "Bar do Ze", "Rua A, 1", new GeoPoint(-23.55, -46.63)), result[0]);
        Assert.Null(result[1].Address);
    }

    [Fact]
    public async Task Nearby_without_results_returns_empty()
    {
        var result = await Client(new StubHandler(HttpStatusCode.OK, "{}")).NearbyAsync(new GeoPoint(0, 0), default);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Details_requests_place_with_field_mask()
    {
        var h = new StubHandler(HttpStatusCode.OK, """
            { "id": "p1", "displayName": { "text": "Bar do Ze" }, "formattedAddress": "Rua A, 1",
              "location": { "latitude": -23.55, "longitude": -46.63 } }
            """);
        var r = await Client(h).DetailsAsync("p1", default);

        Assert.Equal(HttpMethod.Get, h.Request!.Method);
        Assert.Equal("https://places.googleapis.com/v1/places/p1", h.Request.RequestUri!.ToString());
        Assert.Equal("id,displayName,formattedAddress,location", h.Request.Headers.GetValues("X-Goog-FieldMask").Single());
        Assert.Equal("test-key", h.Request.Headers.GetValues("X-Goog-Api-Key").Single());
        Assert.Equal(new PlaceResult("p1", "Bar do Ze", "Rua A, 1", new GeoPoint(-23.55, -46.63)), r);
    }

    [Fact]
    public async Task Details_not_found_returns_null()
    {
        Assert.Null(await Client(new StubHandler(HttpStatusCode.NotFound, "{}")).DetailsAsync("gone", default));
    }

    [Fact]
    public async Task Failure_throws_typed_exception_without_leaking_body()
    {
        var h = new StubHandler(HttpStatusCode.Forbidden, "secret google detail");
        var ex = await Assert.ThrowsAsync<PlacesUnavailableException>(() => Client(h).NearbyAsync(new GeoPoint(0, 0), default));
        Assert.DoesNotContain("secret", ex.Message);
    }

    [Fact]
    public async Task Missing_api_key_throws_typed_exception()
    {
        await Assert.ThrowsAsync<PlacesUnavailableException>(() =>
            Client(new StubHandler(HttpStatusCode.OK, "{}"), key: null).NearbyAsync(new GeoPoint(0, 0), default));
    }

    [Fact]
    public async Task Details_failure_throws_typed_exception()
    {
        await Assert.ThrowsAsync<PlacesUnavailableException>(() =>
            Client(new StubHandler(HttpStatusCode.InternalServerError, "x")).DetailsAsync("p1", default));
    }
}
