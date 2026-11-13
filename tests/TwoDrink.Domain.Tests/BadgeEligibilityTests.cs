using TwoDrink.Domain.Badges;
using TwoDrink.Domain.Scoring;

namespace TwoDrink.Domain.Tests;

public class BadgeEligibilityTests
{
    [Fact] public void First_visit_in_season_grants() => Assert.True(BadgeEligibility.ShouldGrant(Tier.Default, null));
    [Fact] public void Same_tier_does_not_grant() => Assert.False(BadgeEligibility.ShouldGrant(Tier.Bronze, Tier.Bronze));
    [Fact] public void Upgrade_grants_evolution() => Assert.True(BadgeEligibility.ShouldGrant(Tier.Ouro, Tier.Bronze));
    [Fact] public void Downgrade_does_not_grant() => Assert.False(BadgeEligibility.ShouldGrant(Tier.Bronze, Tier.Epic));
    [Fact] public void Renewal_in_same_season_with_lower_or_equal_tier_does_not_grant() {
        Assert.False(BadgeEligibility.ShouldGrant(Tier.Ouro, Tier.Epic));
        Assert.False(BadgeEligibility.ShouldGrant(Tier.Epic, Tier.Epic));
    }
}
