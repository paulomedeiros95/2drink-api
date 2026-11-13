using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace TwoDrink.Infrastructure.Auth;

public class GoogleTokenValidator(IOptions<ExternalAuthOptions> options)
{
    /// <summary>
    /// Bad tokens (including malformed ones) throw <see cref="InvalidJwtException"/>; anything else (missing config,
    /// Google's key endpoint unreachable) is an infrastructure failure.
    /// </summary>
    public async Task<ExternalIdentity> ValidateAsync(string idToken, CancellationToken ct)
    {
        var clientIds = options.Value.Google.ClientIds;
        if (clientIds.Length == 0)
            throw new InvalidOperationException("Auth:Google:ClientIds is not configured.");

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = clientIds,
            });
        }
        catch (Exception e) when (e is FormatException or Newtonsoft.Json.JsonException or ArgumentException)
        {
            // The library parses before validating: undecodable segments surface as parser exceptions.
            throw new InvalidJwtException("Malformed Google ID token: " + e.Message);
        }
        return new ExternalIdentity(ExternalProvider.Google, payload.Subject, payload.Email);
    }
}
