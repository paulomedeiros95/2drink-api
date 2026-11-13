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
    /// <param name="at">Business time for new entries (the caller's validated <c>now</c>).</param>
    public async Task<decimal> ReconcileAsync(CheckIn c, CancellationToken ct, string? reason = null, DateTimeOffset? at = null)
    {
        // Every lookup is scoped to the check-in's account so it stays on the account/check-in indexes.
        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.AccountId == c.LedgerAccountId && e.CheckInId == c.Id
                        && (e.Type == LedgerEntryType.Drink || e.Type == LedgerEntryType.HydrationBonus))
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct);

        // Anything reversed (by the hydration rule or by an admin correction) no longer counts.
        var ids = entries.Select(e => e.Id).ToList();
        var reversed = (await db.LedgerEntries.AsNoTracking()
            .Where(e => e.AccountId == c.LedgerAccountId && e.Type == LedgerEntryType.Reversal
                        && e.ReversesEntryId != null && ids.Contains(e.ReversesEntryId.Value))
            .Select(e => e.ReversesEntryId!.Value)
            .ToListAsync(ct)).ToHashSet();

        var lines = entries
            .Where(e => e.Type == LedgerEntryType.Drink && e.DrinkType is not null && e.Quantity is not null && !reversed.Contains(e.Id))
            .Select(e => new DrinkLine(e.DrinkType!.Value, e.Quantity!.Value))
            .ToList();
        var met = HydrationGoal.IsMet(lines);

        var liveBonus = entries.LastOrDefault(e => e.Type == LedgerEntryType.HydrationBonus && !reversed.Contains(e.Id));
        decimal delta = 0;
        if (met && liveBonus is null)
        {
            delta = rules.HydrationBonus;
            await ledger.AppendAsync(c.LedgerAccountId,
                [new NewEntry(LedgerEntryType.HydrationBonus, delta, VenueId: c.VenueId, CheckInId: c.Id, Reason: reason)], ct, at);
        }
        else if (!met && liveBonus is not null)
        {
            delta = -liveBonus.Points;
            await ledger.AppendAsync(c.LedgerAccountId,
                [new NewEntry(LedgerEntryType.Reversal, delta, VenueId: c.VenueId, CheckInId: c.Id,
                    ReversesEntryId: liveBonus.Id, Reason: GoalLostReason)], ct, at);
        }

        if (c.HydrationMet != met)
        {
            c.HydrationMet = met;
            await db.SaveChangesAsync(ct);
        }
        return delta;
    }
}
