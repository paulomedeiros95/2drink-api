using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TwoDrink.Api.Features.Auth;
using TwoDrink.Api.Startup;
using TwoDrink.Api.Jobs;
using TwoDrink.Infrastructure.Data;

namespace TwoDrink.Api.Features.Leagues;

public static class LeagueEndpoints
{
    public static IServiceCollection AddLeagues(this IServiceCollection services) =>
        services.AddScoped<LeagueService>().AddScoped<StandingsQuery>().AddScoped<TrophyAwarder>();

    static Guid UserId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue(AuthSetup.SubjectClaim)!);

    internal static IResult NotFound(string title, string code) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IEndpointRouteBuilder MapLeagueEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/leagues", async (CreateLeagueRequest req, ClaimsPrincipal principal, LeagueService leagues, CancellationToken ct) =>
            Results.Ok(await leagues.CreateAsync(UserId(principal), req, ct)))
            .RequireAuthorization(AuthSetup.PlayerPolicy)
          .Doc("Leagues", "Criar liga", "Cria uma liga com datas dentro da temporada. 422 league_limit, dates_outside_season etc.")
          .Produces<LeagueSummary>().PlayerProblems(422);

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
        }).RequireAuthorization(AuthSetup.PlayerPolicy)
          .Doc("Leagues", "Criar convite", "Gera link de convite (so o criador). 404 league_not_found; 403 not_league_creator; 422 league_ended.")
          .Produces<InviteResponse>().PlayerProblems(404, 422);

        app.MapPost("/leagues/{leagueId:guid}/renew", async (Guid leagueId, RenewLeagueRequest req, ClaimsPrincipal principal, LeagueService leagues, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await leagues.RenewAsync(UserId(principal), leagueId, req, ct));
            }
            catch (LeagueNotFoundException)
            {
                return NotFound("League not found", "league_not_found");
            }
            catch (NotLeagueCreatorException)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Only the creator can renew",
                    extensions: new Dictionary<string, object?> { ["code"] = "not_league_creator" });
            }
        }).RequireAuthorization(AuthSetup.PlayerPolicy)
          .Doc("Leagues", "Renovar liga", "Cria nova liga com os membros ativos (so o criador; so liga encerrada). 404 league_not_found; 403 not_league_creator; 422 league_not_ended / league_limit.")
          .Produces<RenewLeagueResponse>().PlayerProblems(404, 422);

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
        }).RequireAuthorization(AuthSetup.PlayerPolicy)
          .Doc("Leagues", "Sair da liga", "Marca a saida do jogador. 404 not_member.")
          .Produces(204).PlayerProblems(404);

        app.MapGet("/me/leagues", async (ClaimsPrincipal principal, LeagueService leagues, CancellationToken ct) =>
            Results.Ok(await leagues.MineAsync(UserId(principal), ct)))
            .RequireAuthorization(AuthSetup.PlayerPolicy)
          .Doc("Leagues", "Minhas ligas", "Ligas das quais o jogador participa.")
          .Produces<IReadOnlyList<MyLeague>>().PlayerProblems();

        app.MapGet("/leagues/{leagueId:guid}/standings", async (Guid leagueId, ClaimsPrincipal principal, AppDbContext db, StandingsQuery standings, CancellationToken ct) =>
        {
            var userId = UserId(principal);
            // Unknown league and non-member look the same (404), so league existence is not revealed.
            var member = await db.LeagueMemberships.AsNoTracking().AnyAsync(m => m.LeagueId == leagueId && m.UserId == userId && m.LeftAt == null, ct);
            var items = member ? await standings.ForLeagueAsync(leagueId, ct) : null;
            return items is null ? NotFound("Not a member", "not_member") : Results.Ok(items);
        }).RequireAuthorization(AuthSetup.PlayerPolicy)
          .Doc("Leagues", "Classificacao da liga", "Ranking da liga (empates 1,1,3). 404 not_member para liga desconhecida ou nao-membro.")
          .Produces<IReadOnlyList<StandingItem>>().PlayerProblems(404);

        app.MapGet("/me/trophies", async (ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var userId = UserId(principal);
            var rows = await (from t in db.Trophies.AsNoTracking()
                              join l in db.Leagues.AsNoTracking() on t.LeagueId equals l.Id
                              where t.UserId == userId
                              orderby t.AwardedAt descending, l.Name
                              select new { LeagueId = l.Id, LeagueName = l.Name, t.AwardedAt }).ToListAsync(ct);
            return Results.Ok(rows);
        }).RequireAuthorization(AuthSetup.PlayerPolicy)
          .Doc("Leagues", "Meus trofeus", "Trofeus (Golden Trophy) do jogador, mais novos primeiro.")
          .Produces(200).PlayerProblems();

        return app;
    }
}
