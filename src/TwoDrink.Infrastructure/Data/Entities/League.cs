namespace TwoDrink.Infrastructure.Data.Entities;

/// <summary>A private league. It covers [StartDate 00:00, EndDate+1 00:00) in America/Sao_Paulo, inside one season.</summary>
public class League
{
    public Guid Id { get; set; }
    public Guid SeasonId { get; set; }
    public Guid CreatorId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? CoverBlobName { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTimeOffset? TrophiesAwardedAt { get; set; }
}
