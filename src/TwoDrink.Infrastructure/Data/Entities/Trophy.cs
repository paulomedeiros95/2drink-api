namespace TwoDrink.Infrastructure.Data.Entities;

/// <summary>The Golden Trophy: one per (league, champion), awarded when the league ends.</summary>
public class Trophy
{
    public Guid Id { get; set; }
    public Guid LeagueId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset AwardedAt { get; set; }
}
