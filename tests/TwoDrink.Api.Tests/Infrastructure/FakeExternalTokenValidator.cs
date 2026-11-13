using Microsoft.IdentityModel.Tokens;
using TwoDrink.Infrastructure.Auth;

namespace TwoDrink.Api.Tests.Infrastructure;

/// <summary>
/// Accepts <c>valid:&lt;subject&gt;</c>; <c>unavailable:…</c> fails like a misconfiguration/outage
/// (<see cref="InvalidOperationException"/>); everything else is an invalid token (<see cref="SecurityTokenException"/>).
/// Never calls Google or Apple.
/// </summary>
public class FakeExternalTokenValidator : IExternalTokenValidator
{
    public Task<ExternalIdentity> ValidateAsync(ExternalProvider provider, string token, CancellationToken ct)
    {
        if (token.StartsWith("unavailable:", StringComparison.Ordinal))
            throw new InvalidOperationException("Auth:Google:ClientIds is not configured.");
        const string prefix = "valid:";
        if (!token.StartsWith(prefix, StringComparison.Ordinal) || token.Length == prefix.Length)
            throw new SecurityTokenException("invalid token");
        var subject = token[prefix.Length..];
        return Task.FromResult(new ExternalIdentity(provider, subject, subject + "@example.com"));
    }
}
