using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using TwoDrink.Infrastructure.Auth;

namespace TwoDrink.Api.Tests;

/// <summary>Malformed tokens must surface as "invalid token" (401), never as a misconfiguration (503).</summary>
public class GoogleTokenValidatorTests
{
    static GoogleTokenValidator Validator() =>
        new(Options.Create(new ExternalAuthOptions { Google = new GoogleAuthOptions { ClientIds = ["client.apps.googleusercontent.com"] } }));

    [Theory]
    [InlineData("bad")]
    [InlineData("a.b.c")]
    [InlineData("!!!.@@@.###")]
    [InlineData("eyJhbGciOiJSUzI1NiJ9.e30")]
    [InlineData("eyJhbGciOiJSUzI1NiJ9.bm90LWpzb24.c2ln")]
    public async Task Malformed_tokens_are_invalid_jwt(string token) =>
        await Assert.ThrowsAsync<InvalidJwtException>(() => Validator().ValidateAsync(token, default));
}
