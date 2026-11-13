using System.Security.Claims;
using TwoDrink.Api.Features.Auth;
using TwoDrink.Infrastructure.Storage;

namespace TwoDrink.Api.Features.CheckIns;

public static class CheckInEndpoints
{
    public static IEndpointRouteBuilder MapCheckInEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/checkins/intents/{intentId:guid}/confirm", async (Guid intentId, ConfirmCheckInRequest req,
            ClaimsPrincipal principal, CheckInService checkIns, ILoggerFactory logs, CancellationToken ct) =>
        {
            var userId = Guid.Parse(principal.FindFirstValue(AuthSetup.SubjectClaim)!);
            try
            {
                return Results.Ok(await checkIns.ConfirmAsync(userId, intentId, req, ct));
            }
            catch (CheckInIntentNotFoundException)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Check-in intent not found",
                    extensions: new Dictionary<string, object?> { ["code"] = "intent_not_found" });
            }
            catch (PhotoStorageUnavailableException e)
            {
                logs.CreateLogger("CheckIns").LogWarning(e, "Photo storage unavailable while confirming a check-in");
                return StorageUnavailable();
            }
        }).RequireAuthorization(AuthSetup.PlayerPolicy);

        return app;
    }

    /// <summary>503 <c>storage_unavailable</c>: blob storage failed; the app may retry.</summary>
    public static IResult StorageUnavailable() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Storage unavailable",
            extensions: new Dictionary<string, object?> { ["code"] = "storage_unavailable" });
}
