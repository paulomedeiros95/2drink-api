using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwoDrink.Infrastructure.Auth;
using TwoDrink.Infrastructure.Data;

namespace TwoDrink.Api.Features.Auth;

public static class AuthSetup
{
    public const string PlayerPolicy = "Player";
    public const string SubjectClaim = "sub";

    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.Section));
        services.AddOptions<ExternalAuthOptions>().Bind(config.GetSection(ExternalAuthOptions.Section));
        services.AddSingleton<GoogleTokenValidator>();
        services.AddSingleton<AppleTokenValidator>();
        services.AddSingleton<IExternalTokenValidator, ExternalTokenValidator>();
        services.AddSingleton<JwtIssuer>();
        services.AddScoped<SessionService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        // Configured lazily (after the host is built) so the signing key and clock can be overridden in tests.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((bearer, jwt, time) =>
            {
                var o = jwt.Value;
                bearer.MapInboundClaims = false; // keep "sub" / "ledger" as-is
                bearer.TokenValidationParameters = new()
                {
                    ValidateIssuer = true,
                    ValidIssuer = o.Issuer,
                    ValidateAudience = true,
                    ValidAudience = o.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtIssuer.BuildKey(o),
                    ValidAlgorithms = [Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    // Expiry follows the same clock that issued the token (TestClock in tests).
                    LifetimeValidator = (notBefore, expires, _, p) =>
                    {
                        var now = time.GetUtcNow().UtcDateTime;
                        return (notBefore is null || notBefore.Value - p.ClockSkew <= now) && expires is not null && expires.Value + p.ClockSkew > now;
                    },
                    NameClaimType = SubjectClaim,
                };
            });

        services.AddScoped<IAuthorizationHandler, CompleteProfileHandler>();
        services.AddAuthorizationBuilder().AddPolicy(PlayerPolicy, p => p
            .RequireAuthenticatedUser()
            .AddRequirements(new CompleteProfileRequirement()));
        return services;
    }
}

public class CompleteProfileRequirement : IAuthorizationRequirement;

/// <summary>The "Player" policy: the signed-in user must have a complete profile (read from the database, not the token).</summary>
public class CompleteProfileHandler(AppDbContext db) : AuthorizationHandler<CompleteProfileRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CompleteProfileRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirst(AuthSetup.SubjectClaim)?.Value, out var id))
            return;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
        if (user is { ProfileComplete: true })
            context.Succeed(requirement);
    }
}
