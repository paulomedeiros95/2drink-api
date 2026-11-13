namespace TwoDrink.Infrastructure.Auth;

/// <summary>Production validator: dispatches to the Google or Apple implementation.</summary>
public class ExternalTokenValidator(GoogleTokenValidator google, AppleTokenValidator apple) : IExternalTokenValidator
{
    public Task<ExternalIdentity> ValidateAsync(ExternalProvider provider, string token, CancellationToken ct) =>
        provider switch
        {
            ExternalProvider.Google => google.ValidateAsync(token, ct),
            ExternalProvider.Apple => apple.ValidateAsync(token, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
}
