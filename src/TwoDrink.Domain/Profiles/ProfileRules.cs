using TwoDrink.Domain.Errors;

namespace TwoDrink.Domain.Profiles;

public record ValidatedProfile(string DisplayName, DateOnly BirthDate, string AcceptedTermsVersion, string Locale);

/// <summary>Profile validation: 18+ (as of today in the business time zone), name, terms and locale.</summary>
public static class ProfileRules
{
    public const int MinimumAge = 18;
    public const int MaxDisplayNameLength = 40;
    public const int MaxTermsVersionLength = 32; // matches users.accepted_terms_version varchar(32)
    public static readonly DateOnly EarliestBirthDate = new(1900, 1, 1);
    public static readonly string[] Locales = ["pt-BR", "en"];

    /// <summary>Full years on <paramref name="today"/>. A Feb 29 birthday turns a year older on Mar 1 in non-leap years (CC art. 132 par. 3).</summary>
    public static int AgeOn(DateOnly birth, DateOnly today)
    {
        var age = today.Year - birth.Year;
        if (today.Month < birth.Month || (today.Month == birth.Month && today.Day < birth.Day)) age--;
        return age;
    }

    /// <param name="today">Today's date in the business time zone (see <c>BusinessTime.Today</c>).</param>
    public static ValidatedProfile Validate(string? displayName, DateOnly? birthDate, string? acceptedTermsVersion, string? locale, DateOnly today)
    {
        var name = displayName?.Trim() ?? "";
        if (name.Length is < 1 or > MaxDisplayNameLength)
            throw new RuleViolation("display_name_invalid", $"Display name must have 1 to {MaxDisplayNameLength} characters.");

        var terms = acceptedTermsVersion?.Trim();
        if (string.IsNullOrEmpty(terms) || terms.Length > MaxTermsVersionLength)
            throw new RuleViolation("terms_required", "The terms of use must be accepted.");

        if (locale is null || !Locales.Contains(locale))
            throw new RuleViolation("locale_invalid", "Locale must be pt-BR or en.");

        if (birthDate is not { } birth || birth > today || birth < EarliestBirthDate)
            throw new RuleViolation("invalid_birth_date", "Birth date is not valid.");

        if (AgeOn(birth, today) < MinimumAge)
            throw new RuleViolation("underage", "You must be at least 18 years old.");

        return new ValidatedProfile(name, birth, terms, locale);
    }
}
