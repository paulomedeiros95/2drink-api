using Microsoft.EntityFrameworkCore;
using TwoDrink.Api.Features.Auth;
using TwoDrink.Domain.Profiles;
using TwoDrink.Domain.Rules;
using TwoDrink.Infrastructure.Data;

namespace TwoDrink.Api.Features.Me;

public record ProfileRequest(string? DisplayName, DateOnly? BirthDate, string? AcceptedTermsVersion, string? Locale);

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/me");

        // Any authenticated user may complete their profile; everything else needs the "Player" policy.
        g.MapPut("/profile", async (ProfileRequest req, HttpContext http, AppDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var id = UserId(http);
            var profile = ProfileRules.Validate(req.DisplayName, req.BirthDate, req.AcceptedTermsVersion, req.Locale,
                BusinessTime.Today(time.GetUtcNow()));
            var updated = await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.DisplayName, profile.DisplayName)
                .SetProperty(u => u.BirthDate, profile.BirthDate)
                .SetProperty(u => u.AcceptedTermsVersion, profile.AcceptedTermsVersion)
                .SetProperty(u => u.Locale, profile.Locale), ct);
            return updated == 1 ? Results.Ok(new { profileComplete = true }) : Results.Unauthorized();
        }).RequireAuthorization();

        g.MapGet("", async (HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var id = UserId(http);
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id, ct);
            return Results.Ok(new { id = user.Id, displayName = user.DisplayName, locale = user.Locale });
        }).RequireAuthorization(AuthSetup.PlayerPolicy);

        return app;
    }

    /// <summary>The authenticated user's id (the <c>sub</c> claim). Only call on endpoints that require authorization.</summary>
    public static Guid UserId(HttpContext http) => Guid.Parse(http.User.FindFirst(AuthSetup.SubjectClaim)!.Value);
}
