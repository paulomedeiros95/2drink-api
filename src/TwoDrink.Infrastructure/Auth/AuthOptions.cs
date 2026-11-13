namespace TwoDrink.Infrastructure.Auth;

/// <summary>Section <c>Jwt</c>. <see cref="SigningKey"/> comes from user-secrets / Key Vault, never from committed settings.</summary>
public class JwtOptions
{
    public const string Section = "Jwt";
    public string SigningKey { get; set; } = "";
    public string Issuer { get; set; } = "2drink-api";
    public string Audience { get; set; } = "2drink-app";
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(60);
}

/// <summary>Section <c>Auth</c>: accepted audiences for external identity tokens.</summary>
public class ExternalAuthOptions
{
    public const string Section = "Auth";
    public GoogleAuthOptions Google { get; set; } = new();
    public AppleAuthOptions Apple { get; set; } = new();
}

public class GoogleAuthOptions
{
    public string[] ClientIds { get; set; } = [];
}

public class AppleAuthOptions
{
    /// <summary>Bundle ID (native app) and/or Service ID (web).</summary>
    public string[] Audiences { get; set; } = [];
}
