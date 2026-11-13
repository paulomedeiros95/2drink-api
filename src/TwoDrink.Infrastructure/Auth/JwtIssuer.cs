using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TwoDrink.Infrastructure.Data.Entities;

namespace TwoDrink.Infrastructure.Auth;

/// <summary>Issues the API's own HS256 access tokens. Lifetimes follow the injected <see cref="TimeProvider"/>.</summary>
public class JwtIssuer(IOptions<JwtOptions> options, TimeProvider time)
{
    public const string LedgerClaim = "ledger";

    public static SymmetricSecurityKey BuildKey(JwtOptions o)
    {
        var bytes = Encoding.UTF8.GetBytes(o.SigningKey);
        if (bytes.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");
        return new SymmetricSecurityKey(bytes);
    }

    public string IssueAccessToken(User user)
    {
        var o = options.Value;
        var now = time.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + o.AccessTokenLifetime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(LedgerClaim, user.LedgerAccountId.ToString()),
            ]),
            SigningCredentials = new SigningCredentials(BuildKey(o), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
