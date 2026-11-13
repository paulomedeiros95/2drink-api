namespace TwoDrink.Infrastructure.Data.Entities;

/// <summary>A confirmed check-in at a venue. Drinks are ledger entries; complements are accepted until <see cref="ComplementsUntil"/>.</summary>
public class CheckIn
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid LedgerAccountId { get; set; }
    public Guid VenueId { get; set; }
    public Guid? SeasonId { get; set; }
    public string PhotoBlobName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ComplementsUntil { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public int DrinkCount { get; set; }
    public bool HydrationMet { get; set; }
}
