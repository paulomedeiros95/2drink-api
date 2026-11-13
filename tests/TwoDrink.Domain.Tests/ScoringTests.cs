using TwoDrink.Domain.Rules;
using TwoDrink.Domain.Scoring;

namespace TwoDrink.Domain.Tests;

public class ScoringTests
{
    [Theory]
    [InlineData(DrinkType.Beer, 3, 3.0)]
    [InlineData(DrinkType.Gin, 1, 1.0)]
    [InlineData(DrinkType.Water, 5, 7.5)]
    [InlineData(DrinkType.Soda, 2, 3.0)]
    [InlineData(DrinkType.Whey, 1, 1.5)]
    public void Points_per_drink(DrinkType t, int q, decimal expected) =>
        Assert.Equal(expected, new DrinkCatalog(new()).PointsFor(new(t, q)));

    [Theory]
    [InlineData(Tier.Default, 1)]
    [InlineData(Tier.Bronze, 3)]
    [InlineData(Tier.Prata, 5)]
    [InlineData(Tier.Ouro, 10)]
    [InlineData(Tier.Diamante, 25)]
    [InlineData(Tier.Epic, 50)]
    public void Tier_points(Tier t, decimal p) => Assert.Equal(p, TierPoints.Of(t));

    [Fact]
    public void Hydration_needs_at_least_one_water() =>
        Assert.False(HydrationGoal.IsMet([new(DrinkType.Juice, 3)]));

    [Fact]
    public void Hydration_one_water_alone_is_met() =>
        Assert.True(HydrationGoal.IsMet([new(DrinkType.Water, 1)]));

    [Fact]
    public void Hydration_three_beers_needs_two_waters()
    {
        Assert.False(HydrationGoal.IsMet([new(DrinkType.Beer, 3), new(DrinkType.Water, 1)]));
        Assert.True(HydrationGoal.IsMet([new(DrinkType.Beer, 3), new(DrinkType.Water, 2)]));
    }

    [Fact]
    public void Hydration_juice_does_not_count_as_water() =>
        Assert.False(HydrationGoal.IsMet([new(DrinkType.Beer, 2), new(DrinkType.Juice, 1)]));
}
