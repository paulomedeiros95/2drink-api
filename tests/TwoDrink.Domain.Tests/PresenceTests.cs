using TwoDrink.Domain.Errors;
using TwoDrink.Domain.Presence;
using TwoDrink.Domain.Rules;

namespace TwoDrink.Domain.Tests;

public class PresenceTests
{
    static readonly GeoPoint Bar = new(-23.5505, -46.6333);
    static GeoPoint North(double m) => new(Bar.Lat + m / 111_195.0, Bar.Lng);
    Geofence G => new(new RulesOptions());

    [Fact] public void Inside_70m_ok() => G.EnsureInside(new(North(69), 5, false), Bar);
    [Fact] public void At_100m_with_40m_accuracy_ok() => G.EnsureInside(new(North(100), 40, false), Bar);
    [Fact] public void Accuracy_margin_capped_at_50m() {
        var ex = Assert.Throws<RuleViolation>(() => G.EnsureInside(new(North(130), 800, false), Bar));
        Assert.Equal("out_of_range", ex.Code);
    }
    [Fact] public void Mocked_location_rejected_even_inside() =>
        Assert.Equal("mock_location", Assert.Throws<RuleViolation>(() => G.EnsureInside(new(North(1), 5, true), Bar)).Code);

    [Fact] public void Negative_accuracy_treated_as_zero() {
        var ex = Assert.Throws<RuleViolation>(() => G.EnsureInside(new(North(75), -30, false), Bar));
        Assert.Equal("out_of_range", ex.Code);
    }

    [Fact] public void Exactly_70m_with_zero_accuracy_ok() => G.EnsureInside(new(North(70), 0, false), Bar);
    [Fact] public void Just_over_70m_with_zero_accuracy_rejected() {
        var ex = Assert.Throws<RuleViolation>(() => G.EnsureInside(new(North(70.5), 0, false), Bar));
        Assert.Equal("out_of_range", ex.Code);
    }

    [Fact] public void NaN_latitude_fails_closed() =>
        Assert.Equal("out_of_range", Assert.Throws<RuleViolation>(() => G.EnsureInside(new(new(double.NaN, Bar.Lng), 5, false), Bar)).Code);
    [Fact] public void NaN_longitude_fails_closed() =>
        Assert.Equal("out_of_range", Assert.Throws<RuleViolation>(() => G.EnsureInside(new(new(Bar.Lat, double.NaN), 5, false), Bar)).Code);
    [Fact] public void NaN_accuracy_fails_closed() =>
        Assert.Equal("out_of_range", Assert.Throws<RuleViolation>(() => G.EnsureInside(new(North(1), double.NaN, false), Bar)).Code);
    [Fact] public void Infinite_accuracy_fails_closed() =>
        Assert.Equal("out_of_range", Assert.Throws<RuleViolation>(() => G.EnsureInside(new(North(1), double.PositiveInfinity, false), Bar)).Code);

    [Fact] public void Distance_is_about_111km_per_degree_latitude() {
        var d = Geofence.DistanceMeters(new(0, 0), new(1, 0));
        Assert.InRange(d, 111_100, 111_300);
    }

    [Fact] public void Same_venue_within_15h_rejected() {
        var now = DateTimeOffset.Parse("2027-02-07T10:00:00-03:00");
        var ex = Assert.Throws<RuleViolation>(() => new CheckInCooldown(new()).Ensure(now, now.AddHours(-14.9), now.AddHours(-14.9)));
        Assert.Equal("cooldown_same_venue", ex.Code);
    }
    [Fact] public void Same_venue_exactly_15h_ok() {
        var now = DateTimeOffset.Parse("2027-02-07T10:00:00-03:00");
        new CheckInCooldown(new()).Ensure(now, now.AddHours(-15), null);
    }
    [Fact] public void Other_venue_within_1h_rejected() {
        var now = DateTimeOffset.Parse("2027-02-07T10:00:00-03:00");
        Assert.Equal("cooldown_any_venue",
            Assert.Throws<RuleViolation>(() => new CheckInCooldown(new()).Ensure(now, null, now.AddMinutes(-59))).Code);
    }
    [Fact] public void Other_venue_after_1h_ok() {
        var now = DateTimeOffset.Parse("2027-02-07T10:00:00-03:00");
        new CheckInCooldown(new()).Ensure(now, now.AddHours(-16), now.AddMinutes(-61));
    }
}
