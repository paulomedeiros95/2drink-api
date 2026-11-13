using System.Security.Claims;
using TwoDrink.Api.Features.Auth;

namespace TwoDrink.Api.Features.Leagues;

public static class LeagueEndpoints
{
    public static IServiceCollection AddLeagues(this IServiceCollection services) =>
        services.AddScoped<LeagueService>();

    static Guid UserId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue(AuthSetup.SubjectClaim)!);

    internal static IResult NotFound(string title, string code) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IEndpointRouteBuilder MapLeagueEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/leagues", async (CreateLeagueRequest req, ClaimsPrincipal principal, LeagueService leagues, CancellationToken ct) =>
            Results.Ok(await leagues.CreateAsync(UserId(principal), req, ct)))
            .RequireAuthorization(AuthSetup.PlayerPolicy);

        app.MapPost("/leagues/{leagueId:guid}/invites", async (Guid leagueId, ClaimsPrincipal principal, LeagueService leagues, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await leagues.CreateInviteAsync(UserId(principal), leagueId, ct));
            }
            catch (LeagueNotFoundException)
            {
                return NotFound("League not found", "league_not_found");
            }
            catch (NotLeagueCreatorException)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only the creator can invite",
                    extensions: new Dictionary<string, object?> { ["code"] = "not_league_creator" });
            }
        }).RequireAuthorization(AuthSetup.PlayerPolicy);

        app.MapPost("/leagues/{leagueId:guid}/leave", async (Guid leagueId, ClaimsPrincipal principal, LeagueService leagues, CancellationToken ct) =>
        {
            try
            {
                await leagues.LeaveAsync(UserId(principal), leagueId, ct);
                return Results.NoContent();
            }
            catch (NotMemberException)
            {
                return NotFound("Not a member", "not_member");
            }
        }).RequireAuthorization(AuthSetup.PlayerPolicy);

        app.MapGet("/me/leagues", async (ClaimsPrincipal principal, LeagueService leagues, CancellationToken ct) =>
            Results.Ok(await leagues.MineAsync(UserId(principal), ct)))
            .RequireAuthorization(AuthSetup.PlayerPolicy);

        return app;
    }
}
