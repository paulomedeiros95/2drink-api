using TwoDrink.Infrastructure.Auth;

namespace TwoDrink.Api.Features.Auth;

public record GoogleLoginRequest(string? IdToken);
public record AppleLoginRequest(string? IdentityToken);
public record RefreshRequest(string? RefreshToken);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/auth");
        g.MapPost("/google", (GoogleLoginRequest req, IExternalTokenValidator v, SessionService s, CancellationToken ct) =>
            Login(ExternalProvider.Google, req.IdToken, v, s, ct));
        g.MapPost("/apple", (AppleLoginRequest req, IExternalTokenValidator v, SessionService s, CancellationToken ct) =>
            Login(ExternalProvider.Apple, req.IdentityToken, v, s, ct));
        g.MapPost("/refresh", async (RefreshRequest req, SessionService s, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.RefreshToken))
                return Unauthorized("invalid_refresh_token");
            try
            {
                return Results.Ok(await s.RefreshAsync(req.RefreshToken, ct));
            }
            catch (AuthFailedException)
            {
                return Unauthorized("invalid_refresh_token");
            }
        });
        return app;
    }

    static async Task<IResult> Login(ExternalProvider provider, string? token, IExternalTokenValidator validator,
        SessionService sessions, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Unauthorized("invalid_token");

        ExternalIdentity identity;
        try
        {
            identity = await validator.ValidateAsync(provider, token, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any validation failure (bad signature, wrong audience, expired, malformed) is simply "not authenticated".
            return Unauthorized("invalid_token");
        }
        return Results.Ok(await sessions.LoginAsync(identity, ct));
    }

    static IResult Unauthorized(string code) => Results.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Unauthorized",
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
