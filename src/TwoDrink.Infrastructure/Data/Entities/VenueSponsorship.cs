using TwoDrink.Domain.Scoring;

namespace TwoDrink.Infrastructure.Data.Entities;

/// <summary>Sponsorship tier for a venue over the half-open window [StartsAt, EndsAt). A null art URL means the default art.</summary>
public class VenueSponsorship
{
    public Guid Id { get; set; }
    public Guid VenueId { get; set; }
    public Tier Tier { get; set; }
    public string? BadgeArtUrl { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string CreatedBy { get; set; } = "";
}
