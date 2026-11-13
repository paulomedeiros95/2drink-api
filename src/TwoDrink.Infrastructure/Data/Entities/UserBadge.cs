using TwoDrink.Domain.Scoring;

namespace TwoDrink.Infrastructure.Data.Entities;

/// <summary>
/// A badge a user won at a venue in a season, with the tier and art in force at that moment (null art = default art).
/// One row per tier: an evolution (e.g. Bronze then Ouro) adds a row. Its points live in the ledger.
/// </summary>
public class UserBadge
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid VenueId { get; set; }
    public Guid SeasonId { get; set; }
    public Tier Tier { get; set; }
    public string? ArtUrl { get; set; }
    public Guid CheckInId { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
}
