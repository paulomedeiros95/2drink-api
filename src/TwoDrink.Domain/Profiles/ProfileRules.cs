using TwoDrink.Domain.Errors;

namespace TwoDrink.Domain.Profiles;

public record ValidatedProfile(string DisplayName, DateOnly BirthDate, string AcceptedTermsVersion, string Locale);

/// <summary>Profile validation: 18+ (as of today in the business time zone), name, terms and locale.</summary>
public static class ProfileRules
{
    public const int MinimumAge = 18;
    public const int MaxDisplayNameLength = 40;
    public static readonly DateOnly EarliestBirthDate = new(1900, 1, 1);
    public static readonly string[] Locales = ["pt-BR", "en"];

    /// <param name="today">Today's date in the business time zone (see <c>BusinessTime.Today</c>).</param>
    public static ValidatedProfile Validate(string? displayName, DateOnly? birthDate, string? acceptedTermsVersion, string? locale, DateOnly today)
    {
        var name = displayName?.Trim() ?? "";
        if (name.Length is < 1 or > MaxDisplayNameLength)
            throw new RuleViolation("display_name_invalid", $"Display name must have 1 to {MaxDisplayNameLength} characters.");

        var terms = acceptedTermsVersion?.Trim();
        if (string.IsNullOrEmpty(terms))
            throw new RuleViolation("terms_required", "The terms of use must be accepted.");

        if (locale is null || !Locales.Contains(locale))
            throw new RuleViolation("locale_invalid", "Locale must be pt-BR or en.");

        if (birthDate is not { } birth || birth > today || birth < EarliestBirthDate)
            throw new RuleViolation("invalid_birth_date", "Birth date is not valid.");

        if (birth.AddYears(MinimumAge) > today)
            throw new RuleViolation("underage", "You must be at least 18 years old.");

        return new ValidatedProfile(name, birth, terms, locale);
    }
}
