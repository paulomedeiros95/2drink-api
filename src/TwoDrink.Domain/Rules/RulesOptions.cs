namespace TwoDrink.Domain.Rules;

public sealed record RulesOptions {
    public double RadiusMeters { get; init; } = 70;
    public double MaxAccuracyMarginMeters { get; init; } = 50;
    public TimeSpan SameVenueCooldown { get; init; } = TimeSpan.FromHours(15);
    public TimeSpan AnyVenueCooldown { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan ComplementWindow { get; init; } = TimeSpan.FromHours(12);
    public int MaxDrinksPerCheckIn { get; init; } = 15;
    public decimal AlcoholicPoints { get; init; } = 1m;
    public decimal NonAlcoholicPoints { get; init; } = 1.5m;
    public decimal HydrationBonus { get; init; } = 2m;
    public int MaxActiveLeaguesPerUser { get; init; } = 2;
    public TimeSpan InviteLifetime { get; init; } = TimeSpan.FromDays(7);
}
