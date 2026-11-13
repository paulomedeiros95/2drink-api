using TwoDrink.Domain.Errors;
using TwoDrink.Domain.Rules;

namespace TwoDrink.Domain.Presence;

/// <summary>Intervalos mínimos entre check-ins: no mesmo bar e em qualquer bar.</summary>
public sealed class CheckInCooldown(RulesOptions o)
{
    public void Ensure(DateTimeOffset now, DateTimeOffset? lastAtThisVenue, DateTimeOffset? lastAnywhere)
    {
        if (lastAtThisVenue is { } same && now - same < o.SameVenueCooldown)
            throw new RuleViolation("cooldown_same_venue", "Check-in at this venue is too recent.");

        if (lastAnywhere is { } any && now - any < o.AnyVenueCooldown)
            throw new RuleViolation("cooldown_any_venue", "Check-in at another venue is too recent.");
    }
}
