using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TwoDrink.Api.Features.Auth;
using TwoDrink.Domain.Errors;
using TwoDrink.Infrastructure.Data;

namespace TwoDrink.Api.Features.Me;

public static class LedgerEndpoints
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public static IEndpointRouteBuilder MapLedgerEndpoints(this IEndpointRouteBuilder app)
    {
        // Own statement, newest first. `before` is a Sequence cursor (exclusive). Hashes and admin actors are never exposed.
        app.MapGet("/me/ledger", async (long? before, int? limit, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var take = limit ?? DefaultLimit;
            if (take < 1 || take > MaxLimit || before is < 1)
                throw new RuleViolation("invalid_paging", $"limit must be 1..{MaxLimit} and before a positive sequence.");

            var userId = Guid.Parse(principal.FindFirstValue(AuthSetup.SubjectClaim)!);
            var accountId = await db.Users.Where(u => u.Id == userId).Select(u => u.LedgerAccountId).SingleAsync(ct);

            var query = db.LedgerEntries.AsNoTracking().Where(e => e.AccountId == accountId);
            if (before is { } cursor) query = query.Where(e => e.Sequence < cursor);

            var rows = await query.OrderByDescending(e => e.Sequence).Take(take).ToListAsync(ct);
            return Results.Ok(rows.Select(e => new
            {
                sequence = e.Sequence,
                type = e.Type.ToString(),
                venueId = e.VenueId,
                checkInId = e.CheckInId,
                drinkType = e.DrinkType?.ToString(),
                quantity = e.Quantity,
                tier = e.Tier?.ToString(),
                points = e.Points,
                reason = e.Reason,
                createdAt = e.CreatedAt,
            }));
        }).RequireAuthorization(AuthSetup.PlayerPolicy);

        return app;
    }
}
