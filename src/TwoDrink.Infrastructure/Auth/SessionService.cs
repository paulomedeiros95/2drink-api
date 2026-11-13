using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwoDrink.Infrastructure.Data;
using TwoDrink.Infrastructure.Data.Entities;

namespace TwoDrink.Infrastructure.Auth;

public record AuthTokens(string AccessToken, string RefreshToken, bool ProfileComplete);

/// <summary>The caller could not be authenticated (bad external token, unknown/used/expired refresh token).</summary>
public class AuthFailedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Creates users on first login and issues/rotates the API's own access and refresh tokens.</summary>
public class SessionService(AppDbContext db, JwtIssuer jwt, IOptions<JwtOptions> options, TimeProvider time)
{
    public async Task<AuthTokens> LoginAsync(ExternalIdentity identity, CancellationToken ct)
    {
        var user = await FindUserAsync(identity, ct);
        if (user is null)
        {
            try
            {
                user = await CreateUserAsync(identity, ct);
            }
            catch (DbUpdateException)
            {
                // A concurrent first login for the same identity won the unique index; use that user.
                db.ChangeTracker.Clear();
                var existing = await FindUserAsync(identity, ct);
                if (existing is null) throw;
                user = existing;
            }
        }
        return await IssueAsync(user, ct);
    }

    public async Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var hash = Hash(refreshToken);
        var stored = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null || stored.UsedAt is not null || stored.ExpiresAt <= now)
            throw new AuthFailedException("Invalid refresh token.");

        // Atomic claim: of two concurrent refreshes with the same token only one updates a row.
        var claimed = await db.RefreshTokens
            .Where(t => t.Id == stored.Id && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);
        if (claimed != 1)
            throw new AuthFailedException("Invalid refresh token.");

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == stored.UserId, ct)
                   ?? throw new AuthFailedException("Invalid refresh token.");
        return await IssueAsync(user, ct);
    }

    Task<User?> FindUserAsync(ExternalIdentity identity, CancellationToken ct) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Provider == identity.Provider && u.Subject == identity.Subject, ct);

    async Task<User> CreateUserAsync(ExternalIdentity identity, CancellationToken ct)
    {
        var account = new LedgerAccount { Id = Guid.NewGuid() };
        var user = new User
        {
            Id = Guid.NewGuid(),
            Provider = identity.Provider,
            Subject = identity.Subject,
            Email = identity.Email,
            LedgerAccountId = account.Id,
            CreatedAt = time.GetUtcNow(),
        };
        db.LedgerAccounts.Add(account);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    async Task<AuthTokens> IssueAsync(User user, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var raw = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = Hash(raw),
            CreatedAt = now,
            ExpiresAt = now + options.Value.RefreshTokenLifetime,
        });
        await db.SaveChangesAsync(ct);
        return new AuthTokens(jwt.IssueAccessToken(user), raw, user.ProfileComplete);
    }

    static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
