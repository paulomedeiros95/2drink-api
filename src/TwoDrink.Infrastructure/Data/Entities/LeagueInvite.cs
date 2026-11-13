namespace TwoDrink.Infrastructure.Data.Entities;

/// <summary>Single-use invite. Only the SHA-256 hex hash of the token is stored.</summary>
public class LeagueInvite
{
    public Guid Id { get; set; }
    public Guid LeagueId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? UsedBy { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}
