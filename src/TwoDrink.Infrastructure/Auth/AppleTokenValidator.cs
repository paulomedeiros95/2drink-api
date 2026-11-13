using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace TwoDrink.Infrastructure.Auth;

/// <summary>Validates Sign in with Apple identity tokens against Apple's published (cached) signing keys.</summary>
public class AppleTokenValidator(IOptions<ExternalAuthOptions> options)
{
    public const string Issuer = "https://appleid.apple.com";
    const string MetadataAddress = Issuer + "/.well-known/openid-configuration";

    // ConfigurationManager caches the metadata/JWKS and refreshes it on its own schedule.
    static readonly ConfigurationManager<OpenIdConnectConfiguration> Configuration = new(
        MetadataAddress, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever { RequireHttps = true });

    public async Task<ExternalIdentity> ValidateAsync(string identityToken, CancellationToken ct)
    {
        var audiences = options.Value.Apple.Audiences;
        if (audiences.Length == 0)
            throw new InvalidOperationException("Auth:Apple:Audiences is not configured.");

        var config = await Configuration.GetConfigurationAsync(ct);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(identityToken, new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudiences = audiences,
            IssuerSigningKeys = config.SigningKeys,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        });
        if (!result.IsValid)
            throw new SecurityTokenException("Invalid Apple identity token.", result.Exception);

        var jwt = (JsonWebToken)result.SecurityToken;
        var email = jwt.TryGetPayloadValue<string>("email", out var e) ? e : null;
        return new ExternalIdentity(ExternalProvider.Apple, jwt.Subject, email);
    }
}
