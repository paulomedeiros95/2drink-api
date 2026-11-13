namespace TwoDrink.Infrastructure.Data.Entities;

/// <summary>A bar known from Google Places, cached locally. <see cref="PlaceId"/> is unique.</summary>
public class Venue
{
    public Guid Id { get; set; }
    public string PlaceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}
