namespace TwoDrink.Infrastructure.Data.Entities;

/// <summary>Only the SHA-256 hash of the token is stored. A token is single use: rotation sets <see cref="UsedAt"/>.</summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}
