using TwoDrink.Domain.Presence;

namespace TwoDrink.Infrastructure.Places;

public record PlaceResult(string PlaceId, string Name, string? Address, GeoPoint Location);

/// <summary>Google Places (New) access. Failures surface as <see cref="PlacesUnavailableException"/>.</summary>
public interface IPlacesClient
{
    Task<IReadOnlyList<PlaceResult>> NearbyAsync(GeoPoint p, CancellationToken ct);

    /// <summary>Returns null when the place no longer exists.</summary>
    Task<PlaceResult?> DetailsAsync(string placeId, CancellationToken ct);
}

/// <summary>Google Places could not be reached or answered with an error. The message never carries Google's response.</summary>
public class PlacesUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
