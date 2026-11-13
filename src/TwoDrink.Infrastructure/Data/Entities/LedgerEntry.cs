using TwoDrink.Domain.Scoring;

namespace TwoDrink.Infrastructure.Data.Entities;

public enum LedgerEntryType { CheckIn, Drink, Badge, HydrationBonus, Reversal }

/// <summary>One immutable, hash-chained line of the points statement. Contains no personal data.</summary>
public class LedgerEntry
{
    public long Id { get; set; }
    public Guid AccountId { get; set; }
    public long Sequence { get; set; }
    public LedgerEntryType Type { get; set; }
    public Guid? VenueId { get; set; }
    public Guid? CheckInId { get; set; }
    public DrinkType? DrinkType { get; set; }
    public int? Quantity { get; set; }
    public Tier? Tier { get; set; }
    public decimal Points { get; set; }
    public long? ReversesEntryId { get; set; }
    public string? Reason { get; set; }
    public string? ActorAdmin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string PrevHash { get; set; } = "";
    public string Hash { get; set; } = "";
}
