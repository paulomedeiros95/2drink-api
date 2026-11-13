namespace TwoDrink.Infrastructure.Auth;

public enum ExternalProvider { Google, Apple }

public record ExternalIdentity(ExternalProvider Provider, string Subject, string? Email);

/// <summary>Validates a Google/Apple identity token and returns who it belongs to. Throws if the token is not valid.</summary>
public interface IExternalTokenValidator
{
    Task<ExternalIdentity> ValidateAsync(ExternalProvider provider, string token, CancellationToken ct);
}
