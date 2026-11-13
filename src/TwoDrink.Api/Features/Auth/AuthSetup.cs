using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
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
        services.AddSingleton(sp => new AppleTokenValidator(sp.GetRequiredService<IOptions<ExternalAuthOptions>>()));
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
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();
        services.AddAuthorizationBuilder()
            // Secure by default: an endpoint without its own policy still needs a signed-in user
            // (/auth/* and /health opt out with AllowAnonymous).
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(PlayerPolicy, p => p
                .RequireAuthenticatedUser()
                .AddRequirements(new CompleteProfileRequirement()));
        return services;
    }
}

public class CompleteProfileRequirement : IAuthorizationRequirement;

/// <summary>
/// A "Player" 403 caused by an incomplete profile becomes ProblemDetails with <c>code = profile_incomplete</c>, so the app
/// can send the user to the profile screen. Everything else keeps the default behaviour.
/// </summary>
public class ProblemAuthorizationResultHandler(IProblemDetailsService problems) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<CompleteProfileRequirement>().Any() == true)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await problems.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails =
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Profile incomplete",
                    Detail = "Complete your profile to use this feature.",
                    Extensions = { ["code"] = "profile_incomplete" },
                },
            });
            return;
        }
        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

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
