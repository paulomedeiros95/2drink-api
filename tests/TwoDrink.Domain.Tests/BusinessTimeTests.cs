using TwoDrink.Domain.Rules;

namespace TwoDrink.Domain.Tests;

public class BusinessTimeTests
{
    [Fact] public void Range_covers_end_day_until_midnight_sao_paulo() {
        var (s, e) = BusinessTime.Range(new(2027, 2, 1), new(2027, 2, 15));
        Assert.Equal(DateTimeOffset.Parse("2027-02-01T03:00:00Z"), s);
        Assert.Equal(DateTimeOffset.Parse("2027-02-16T03:00:00Z"), e);
    }

    [Fact] public void Zone_is_sao_paulo() =>
        Assert.Equal("America/Sao_Paulo", BusinessTime.Zone.Id);

    [Fact] public void Today_uses_business_zone_at_utc_boundary() =>
        Assert.Equal(new DateOnly(2027, 2, 6), BusinessTime.Today(DateTimeOffset.Parse("2027-02-07T02:30:00Z")));
}
