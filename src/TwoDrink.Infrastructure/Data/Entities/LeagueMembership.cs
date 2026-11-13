namespace TwoDrink.Infrastructure.Data.Entities;

/// <summary>PK (LeagueId, UserId): a person who left can never join the same league again.</summary>
public class LeagueMembership
{
    public Guid LeagueId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
}
