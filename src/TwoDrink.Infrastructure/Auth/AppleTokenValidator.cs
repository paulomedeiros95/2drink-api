using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace TwoDrink.Infrastructure.Auth;

/// <summary>Validates Sign in with Apple identity tokens against Apple's published (cached) signing keys.</summary>
public class AppleTokenValidator(IOptions<ExternalAuthOptions> options, IConfigurationManager<OpenIdConnectConfiguration> configuration)
{
    public const string Issuer = "https://appleid.apple.com";
    const string MetadataAddress = Issuer + "/.well-known/openid-configuration";

    /// <summary>Apple's metadata/JWKS, cached process-wide and refreshed on its own schedule (or on demand after a key rotation).</summary>
    public static readonly IConfigurationManager<OpenIdConnectConfiguration> AppleConfiguration =
        new ConfigurationManager<OpenIdConnectConfiguration>(
            MetadataAddress, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever { RequireHttps = true });

    /// <summary>Production constructor: Apple's real (cached) keys.</summary>
    public AppleTokenValidator(IOptions<ExternalAuthOptions> options) : this(options, AppleConfiguration)
    {
    }

    public async Task<ExternalIdentity> ValidateAsync(string identityToken, CancellationToken ct)
    {
        var audiences = options.Value.Apple.Audiences;
        if (audiences.Length == 0)
            throw new InvalidOperationException("Auth:Apple:Audiences is not configured.");

        var result = await ValidateWithCurrentKeysAsync(identityToken, audiences, ct);
        if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            // Apple rotated its keys after we cached them: refresh once and retry.
            configuration.RequestRefresh();
            result = await ValidateWithCurrentKeysAsync(identityToken, audiences, ct);
        }
        if (!result.IsValid)
            throw new SecurityTokenException("Invalid Apple identity token.", result.Exception);

        var jwt = (JsonWebToken)result.SecurityToken;
        var email = jwt.TryGetPayloadValue<string>("email", out var e) ? e : null;
        return new ExternalIdentity(ExternalProvider.Apple, jwt.Subject, email);
    }

    async Task<TokenValidationResult> ValidateWithCurrentKeysAsync(string identityToken, string[] audiences, CancellationToken ct)
    {
        var config = await configuration.GetConfigurationAsync(ct);
        return await new JsonWebTokenHandler().ValidateTokenAsync(identityToken, new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudiences = audiences,
            IssuerSigningKeys = config.SigningKeys,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        });
    }
}
