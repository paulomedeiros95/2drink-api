using System.Security.Claims;
using TwoDrink.Api.Features.Auth;
using TwoDrink.Api.Startup;
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
        }).RequireAuthorization(AuthSetup.PlayerPolicy)
          .Doc("CheckIns", "Confirmar check-in", "Depois do upload do JPEG: confirma o intent, congela a selfie e credita os pontos. Idempotente. 404 intent_not_found; 422 photo_invalid e regras de bebida; 503 storage_unavailable.")
          .Produces<ConfirmCheckInResponse>().PlayerProblems(404, 422, 503);

        app.MapPost("/checkins/{checkInId:guid}/drinks", async (Guid checkInId, ComplementRequest req,
            ClaimsPrincipal principal, CheckInService checkIns, CancellationToken ct) =>
        {
            var userId = Guid.Parse(principal.FindFirstValue(AuthSetup.SubjectClaim)!);
            try
            {
                return Results.Ok(await checkIns.ComplementAsync(userId, checkInId, req, ct));
            }
            catch (CheckInNotFoundException)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Check-in not found",
                    extensions: new Dictionary<string, object?> { ["code"] = "checkin_not_found" });
            }
        }).RequireAuthorization(AuthSetup.PlayerPolicy)
          .Doc("CheckIns", "Adicionar bebidas a um check-in", "Complemento de bebidas ate fechar a janela do check-in. 404 checkin_not_found; 422 checkin_closed / drink_limit.")
          .Produces<ComplementResponse>().PlayerProblems(404, 422);

        return app;
    }

    /// <summary>503 <c>storage_unavailable</c>: blob storage failed; the app may retry.</summary>
    public static IResult StorageUnavailable() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Storage unavailable",
            extensions: new Dictionary<string, object?> { ["code"] = "storage_unavailable" });
}
