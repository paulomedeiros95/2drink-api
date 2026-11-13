using Microsoft.EntityFrameworkCore;
using TwoDrink.Domain.Rules;
using TwoDrink.Domain.Scoring;
using TwoDrink.Infrastructure.Data;
using TwoDrink.Infrastructure.Data.Entities;
using TwoDrink.Infrastructure.Ledger;

namespace TwoDrink.Api.Features.CheckIns;

/// <summary>
/// Keeps the hydration bonus of a check-in in line with its drinks: +bonus when the goal is met, a reversal when a
/// later complement undoes it. Runs inside the caller's transaction, under the per-user check-in lock.
/// </summary>
public class HydrationReconciler(AppDbContext db, LedgerWriter ledger, RulesOptions rules)
{
    public const string GoalLostReason = "hydration_goal_lost";

    /// <summary>Reconciles <paramref name="c"/>; returns the points added (+bonus, -bonus or 0).</summary>
    /// <param name="reason">Stored on a new bonus entry (reversals always use <see cref="GoalLostReason"/>).</param>
    public async Task<decimal> ReconcileAsync(CheckIn c, CancellationToken ct, string? reason = null)
    {
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.CheckInId == c.Id && (e.Type == LedgerEntryType.Drink
                || e.Type == LedgerEntryType.HydrationBonus || e.Type == LedgerEntryType.Reversal))
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct);

        var lines = entries.Where(e => e.Type == LedgerEntryType.Drink && e.DrinkType is not null && e.Quantity is not null)
            .Select(e => new DrinkLine(e.DrinkType!.Value, e.Quantity!.Value))
            .ToList();
        var met = HydrationGoal.IsMet(lines);

        var balance = entries.Where(e => e.Type != LedgerEntryType.Drink).Sum(e => e.Points);
        decimal delta = 0;
        if (met && balance == 0)
        {
            delta = rules.HydrationBonus;
            await ledger.AppendAsync(c.LedgerAccountId,
                [new NewEntry(LedgerEntryType.HydrationBonus, delta, VenueId: c.VenueId, CheckInId: c.Id, Reason: reason)], ct);
        }
        else if (!met && balance > 0)
        {
            var reversed = entries.Where(e => e.Type == LedgerEntryType.Reversal && e.ReversesEntryId is not null)
                .Select(e => e.ReversesEntryId!.Value).ToHashSet();
            var bonus = entries.Last(e => e.Type == LedgerEntryType.HydrationBonus && !reversed.Contains(e.Id));
            delta = -bonus.Points;
            await ledger.AppendAsync(c.LedgerAccountId,
                [new NewEntry(LedgerEntryType.Reversal, delta, VenueId: c.VenueId, CheckInId: c.Id,
                    ReversesEntryId: bonus.Id, Reason: GoalLostReason)], ct);
        }

        if (c.HydrationMet != met)
        {
            c.HydrationMet = met;
            await db.SaveChangesAsync(ct);
        }
        return delta;
    }
}
