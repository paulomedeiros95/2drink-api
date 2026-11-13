using Google.Apis.Auth;
using Microsoft.IdentityModel.Tokens;
using TwoDrink.Infrastructure.Auth;

namespace TwoDrink.Api.Features.Auth;

public record GoogleLoginRequest(string? IdToken);
public record AppleLoginRequest(string? IdentityToken);
public record RefreshRequest(string? RefreshToken);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/auth").AllowAnonymous();
        g.MapPost("/google", (GoogleLoginRequest req, IExternalTokenValidator v, SessionService s, ILoggerFactory logs, CancellationToken ct) =>
            Login(ExternalProvider.Google, req.IdToken, v, s, logs, ct));
        g.MapPost("/apple", (AppleLoginRequest req, IExternalTokenValidator v, SessionService s, ILoggerFactory logs, CancellationToken ct) =>
            Login(ExternalProvider.Apple, req.IdentityToken, v, s, logs, ct));
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
        SessionService sessions, ILoggerFactory logs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Unauthorized("invalid_token");

        ExternalIdentity identity;
        try
        {
            identity = await validator.ValidateAsync(provider, token, ct);
        }
        catch (Exception ex) when (ex is SecurityTokenException or InvalidJwtException)
        {
            // The token itself is bad (signature, audience, expiry, malformed): simply "not authenticated".
            return Unauthorized("invalid_token");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Missing audiences, key endpoint unreachable, ...: our problem, not the client's.
            logs.CreateLogger("Auth").LogError(ex, "Could not validate a {Provider} identity token", provider);
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Sign-in unavailable",
                extensions: new Dictionary<string, object?> { ["code"] = "auth_unavailable" });
        }
        return Results.Ok(await sessions.LoginAsync(identity, ct));
    }

    static IResult Unauthorized(string code) => Results.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Unauthorized",
        extensions: new Dictionary<string, object?> { ["code"] = code });
}
