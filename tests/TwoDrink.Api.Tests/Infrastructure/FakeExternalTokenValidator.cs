using TwoDrink.Infrastructure.Auth;

namespace TwoDrink.Api.Tests.Infrastructure;

/// <summary>Accepts <c>valid:&lt;subject&gt;</c>; rejects everything else. Never calls Google or Apple.</summary>
public class FakeExternalTokenValidator : IExternalTokenValidator
{
    public Task<ExternalIdentity> ValidateAsync(ExternalProvider provider, string token, CancellationToken ct)
    {
        const string prefix = "valid:";
        if (!token.StartsWith(prefix, StringComparison.Ordinal) || token.Length == prefix.Length)
            throw new InvalidOperationException("invalid token");
        var subject = token[prefix.Length..];
        return Task.FromResult(new ExternalIdentity(provider, subject, subject + "@example.com"));
    }
}
