using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using TwoDrink.Infrastructure.Auth;

namespace TwoDrink.Api.Tests;

/// <summary>Apple rotates its signing keys: an unknown <c>kid</c> must refresh the cached JWKS and retry once.</summary>
public class AppleTokenValidatorTests
{
    const string Audience = "app.2drink";

    /// <summary>Serves <c>before</c> until a refresh is requested, then <c>after</c>.</summary>
    sealed class RotatingConfiguration(SecurityKey before, SecurityKey after) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public int Refreshes;

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            var config = new OpenIdConnectConfiguration();
            config.SigningKeys.Add(Refreshes == 0 ? before : after);
            return Task.FromResult(config);
        }

        public void RequestRefresh() => Refreshes++;
    }

    static RsaSecurityKey NewKey(string kid) => new(RSA.Create(2048)) { KeyId = kid };

    static string Token(SecurityKey key) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = AppleTokenValidator.Issuer,
        Audience = Audience,
        Claims = new Dictionary<string, object> { ["sub"] = "apple-user" },
        Expires = DateTime.UtcNow.AddMinutes(5),
        SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
    });

    static AppleTokenValidator Validator(IConfigurationManager<OpenIdConnectConfiguration> config) =>
        new(Options.Create(new ExternalAuthOptions { Apple = new AppleAuthOptions { Audiences = [Audience] } }), config);

    [Fact]
    public async Task Unknown_key_refreshes_the_keys_and_retries_once()
    {
        var oldKey = NewKey("old");
        var newKey = NewKey("new");
        var config = new RotatingConfiguration(oldKey, newKey);

        var identity = await Validator(config).ValidateAsync(Token(newKey), default);

        Assert.Equal("apple-user", identity.Subject);
        Assert.Equal(1, config.Refreshes);
    }

    [Fact]
    public async Task Token_signed_by_a_key_apple_never_published_is_rejected_after_one_refresh()
    {
        var config = new RotatingConfiguration(NewKey("old"), NewKey("new"));

        await Assert.ThrowsAnyAsync<SecurityTokenException>(() => Validator(config).ValidateAsync(Token(NewKey("forged")), default));
        Assert.Equal(1, config.Refreshes);
    }
}
