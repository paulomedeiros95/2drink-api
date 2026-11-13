using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TwoDrink.Domain.Presence;

namespace TwoDrink.Infrastructure.Places;

/// <summary>Typed HttpClient for Google Places API (New). The key comes from <c>Google:PlacesApiKey</c> (never committed).</summary>
public sealed class GooglePlacesClient(HttpClient http, IConfiguration config) : IPlacesClient
{
    private const string NearbyUrl = "https://places.googleapis.com/v1/places:searchNearby";
    private const string PlaceUrl = "https://places.googleapis.com/v1/places/";
    private const string NearbyFields = "places.id,places.displayName,places.formattedAddress,places.location";
    private const string DetailsFields = "id,displayName,formattedAddress,location";
    private const double RadiusMeters = 300;

    public async Task<IReadOnlyList<PlaceResult>> NearbyAsync(GeoPoint p, CancellationToken ct)
    {
        using var req = NewRequest(HttpMethod.Post, NearbyUrl, NearbyFields);
        req.Content = JsonContent.Create(new
        {
            includedTypes = new[] { "bar", "restaurant" },
            maxResultCount = 20,
            locationRestriction = new
            {
                circle = new { center = new { latitude = p.Lat, longitude = p.Lng }, radius = RadiusMeters },
            },
        });

        using var doc = await SendAsync(req, ct) ?? throw new PlacesUnavailableException("Google Places returned no content.");
        var list = new List<PlaceResult>();
        if (doc.RootElement.TryGetProperty("places", out var places) && places.ValueKind == JsonValueKind.Array)
            foreach (var el in places.EnumerateArray())
                if (Map(el) is { } place) list.Add(place);
        return list;
    }

    public async Task<PlaceResult?> DetailsAsync(string placeId, CancellationToken ct)
    {
        using var req = NewRequest(HttpMethod.Get, PlaceUrl + Uri.EscapeDataString(placeId), DetailsFields);
        using var doc = await SendAsync(req, ct, notFoundIsNull: true);
        return doc is null ? null : Map(doc.RootElement);
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string url, string fieldMask)
    {
        var key = config["Google:PlacesApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            throw new PlacesUnavailableException("Google Places API key is not configured.");
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-Goog-Api-Key", key);
        req.Headers.Add("X-Goog-FieldMask", fieldMask);
        return req;
    }

    private async Task<JsonDocument?> SendAsync(HttpRequestMessage req, CancellationToken ct, bool notFoundIsNull = false)
    {
        try
        {
            using var res = await http.SendAsync(req, ct);
            if (notFoundIsNull && res.StatusCode == HttpStatusCode.NotFound) return null;
            if (!res.IsSuccessStatusCode)
                throw new PlacesUnavailableException($"Google Places answered {(int)res.StatusCode}.");
            return await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new PlacesUnavailableException("Google Places request failed.", e);
        }
    }

    private static PlaceResult? Map(JsonElement el)
    {
        if (!el.TryGetProperty("id", out var id) || id.GetString() is not { Length: > 0 } placeId) return null;
        if (!el.TryGetProperty("location", out var loc)
            || !loc.TryGetProperty("latitude", out var lat) || !loc.TryGetProperty("longitude", out var lng)) return null;
        var name = el.TryGetProperty("displayName", out var dn) && dn.TryGetProperty("text", out var t) ? t.GetString() : null;
        var address = el.TryGetProperty("formattedAddress", out var fa) ? fa.GetString() : null;
        return new PlaceResult(placeId, string.IsNullOrWhiteSpace(name) ? placeId : name, address,
            new GeoPoint(lat.GetDouble(), lng.GetDouble()));
    }
}
