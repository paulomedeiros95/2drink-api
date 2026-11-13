namespace TwoDrink.Infrastructure.Data.Entities;

/// <summary>A validated promise to check in: the presence check passed and a selfie upload slot (<see cref="BlobName"/>) was issued.</summary>
public class CheckInIntent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid VenueId { get; set; }
    /// <summary><c>checkins/{Id}.jpg</c></summary>
    public string BlobName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? ConfirmedCheckInId { get; set; }
}
