using Microsoft.EntityFrameworkCore;
using TwoDrink.Api.Startup;
using TwoDrink.Domain.Profiles;
using TwoDrink.Domain.Rules;
using TwoDrink.Infrastructure.Auth;
using TwoDrink.Infrastructure.Data;

namespace TwoDrink.Api.Features.Auth;

public record DevTokenRequest(string? Subject, string? DisplayName, DateOnly? BirthDate, bool? CompleteProfile);

/// <summary>
/// <c>POST /dev/token</c>: signs a user in without a real Google/Apple token, for trying the API in Swagger.
/// Registered only when the environment is Development AND <c>DevAuth:Enabled=true</c>; in every other case the route does not exist.
/// </summary>
public static class DevAuthEndpoints
{
    public const string SubjectPrefix = "dev:";

    public static bool IsEnabled(IHostEnvironment env, IConfiguration config) =>
        env.IsDevelopment() && config.GetValue("DevAuth:Enabled", false);

    public static IEndpointRouteBuilder MapDevAuthEndpoints(this WebApplication app)
    {
        if (!IsEnabled(app.Environment, app.Configuration)) return app;

        app.MapPost("/dev/token", async (DevTokenRequest req, SessionService sessions, AppDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var subject = req.Subject?.Trim();
            if (string.IsNullOrEmpty(subject) || subject.Length > 100)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "subject is required (max 100 chars)");

            var tokens = await sessions.LoginAsync(new ExternalIdentity(ExternalProvider.Google, SubjectPrefix + subject, null), ct);
            if (req.CompleteProfile != true) return Results.Ok(tokens);

            var today = BusinessTime.Today(time.GetUtcNow());
            var profile = ProfileRules.Validate(
                string.IsNullOrWhiteSpace(req.DisplayName) ? subject : req.DisplayName.Trim(),
                req.BirthDate ?? today.AddYears(-30), "dev", "pt-BR", today);
            var dbSubject = SubjectPrefix + subject;
            await db.Users.Where(u => u.Provider == ExternalProvider.Google && u.Subject == dbSubject).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.DisplayName, profile.DisplayName)
                .SetProperty(u => u.BirthDate, profile.BirthDate)
                .SetProperty(u => u.AcceptedTermsVersion, profile.AcceptedTermsVersion)
                .SetProperty(u => u.Locale, profile.Locale), ct);
            return Results.Ok(tokens with { ProfileComplete = true });
        }).AllowAnonymous()
          .Doc("Dev", "Entrar sem Google/Apple (somente desenvolvimento)",
              "Cria ou encontra o usuario `dev:<subject>` e devolve tokens reais da API. Com completeProfile=true o perfil adulto e preenchido (termos \"dev\", locale pt-BR). Existe apenas em Development com DevAuth:Enabled=true.")
          .Produces<AuthTokens>()
          .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}
