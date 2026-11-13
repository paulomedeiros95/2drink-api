using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace TwoDrink.Infrastructure.Auth;

public class GoogleTokenValidator(IOptions<ExternalAuthOptions> options)
{
    public async Task<ExternalIdentity> ValidateAsync(string idToken, CancellationToken ct)
    {
        var clientIds = options.Value.Google.ClientIds;
        if (clientIds.Length == 0)
            throw new InvalidOperationException("Auth:Google:ClientIds is not configured.");

        var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = clientIds,
        });
        return new ExternalIdentity(ExternalProvider.Google, payload.Subject, payload.Email);
    }
}
