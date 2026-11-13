using System.Security.Claims;
using TwoDrink.Api.Features.Auth;

namespace TwoDrink.Api.Features.Leagues;

public static class InviteEndpoints
{
    public static IEndpointRouteBuilder MapInviteEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/invites/{token}/accept", async (string token, ClaimsPrincipal principal, LeagueService leagues, CancellationToken ct) =>
        {
            var userId = Guid.Parse(principal.FindFirstValue(AuthSetup.SubjectClaim)!);
            try
            {
                return Results.Ok(await leagues.AcceptAsync(userId, token, ct));
            }
            catch (InviteInvalidException)
            {
                return LeagueEndpoints.NotFound("Invite not found", "invite_invalid");
            }
        }).RequireAuthorization(AuthSetup.PlayerPolicy);

        return app;
    }
}
