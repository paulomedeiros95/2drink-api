using TwoDrink.Infrastructure.Auth;

namespace TwoDrink.Infrastructure.Data.Entities;

public class User
{
    public Guid Id { get; set; }
    public ExternalProvider Provider { get; set; }
    public string Subject { get; set; } = "";
    public Guid LedgerAccountId { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? AcceptedTermsVersion { get; set; }
    public string Locale { get; set; } = "pt-BR";
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>True when display name, birth date and accepted terms are all filled in.</summary>
    public bool ProfileComplete =>
        !string.IsNullOrWhiteSpace(DisplayName) && BirthDate is not null && !string.IsNullOrWhiteSpace(AcceptedTermsVersion);
}
