using TwoDrink.Domain.Errors;
using TwoDrink.Domain.Profiles;

namespace TwoDrink.Domain.Tests;

public class ProfileRulesTests
{
    static ValidatedProfile Validate(DateOnly birth, DateOnly today, string terms = "v1") =>
        ProfileRules.Validate("Ana", birth, terms, "pt-BR", today);

    [Fact]
    public void Leap_day_birthday_is_underage_on_feb_28_of_non_leap_year()
    {
        var ex = Assert.Throws<RuleViolation>(() => Validate(new DateOnly(2008, 2, 29), new DateOnly(2026, 2, 28)));
        Assert.Equal("underage", ex.Code);
    }

    [Fact]
    public void Leap_day_birthday_is_adult_on_mar_1_of_non_leap_year() =>
        Validate(new DateOnly(2008, 2, 29), new DateOnly(2026, 3, 1));

    [Fact]
    public void Feb_28_birthday_is_adult_on_feb_28() =>
        Validate(new DateOnly(2008, 2, 28), new DateOnly(2026, 2, 28));

    [Fact]
    public void Terms_version_longer_than_32_is_rejected()
    {
        var ex = Assert.Throws<RuleViolation>(() => Validate(new DateOnly(1990, 1, 1), new DateOnly(2027, 2, 6), new string('v', 33)));
        Assert.Equal("terms_required", ex.Code);
        Validate(new DateOnly(1990, 1, 1), new DateOnly(2027, 2, 6), new string('v', 32));
    }
}
