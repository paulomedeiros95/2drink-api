namespace TwoDrink.Infrastructure.Data.Entities;

/// <summary>A season covers [StartDate 00:00, EndDate+1 00:00) in America/Sao_Paulo.</summary>
public class Season
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}
