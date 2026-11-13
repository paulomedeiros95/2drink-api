using Microsoft.EntityFrameworkCore;
using TwoDrink.Domain.Scoring;
using TwoDrink.Infrastructure.Data;
using TwoDrink.Infrastructure.Data.Entities;

namespace TwoDrink.Infrastructure.Ledger;

public record NewEntry(
    LedgerEntryType Type,
    decimal Points,
    Guid? VenueId = null,
    Guid? CheckInId = null,
    DrinkType? DrinkType = null,
    int? Quantity = null,
    Tier? Tier = null,
    long? ReversesEntryId = null,
    string? Reason = null,
    string? ActorAdmin = null);

public class LedgerWriter(AppDbContext db, TimeProvider clock)
{
    /// <summary>Appends entries to the account chain. Must run inside the caller's transaction.</summary>
    /// <param name="at">
    /// Business time of the entries (the <c>now</c> the caller validated its rules with), so an entry never lands after the
    /// window it was validated for. Defaults to the clock at append time. Truncated to microseconds like every timestamp.
    /// </param>
    public async Task<IReadOnlyList<LedgerEntry>> AppendAsync(
        Guid accountId, IReadOnlyList<NewEntry> entries, CancellationToken ct, DateTimeOffset? at = null)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("LedgerWriter.AppendAsync must be called inside a transaction.");

        // Serialises writers per account until the transaction ends, so the chain cannot fork.
        var key = accountId.ToString("D");
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLocks.Ledger}, hashtext({key}))", ct);

        var last = await db.LedgerEntries
            .Where(x => x.AccountId == accountId)
            .OrderByDescending(x => x.Sequence)
            .Select(x => new { x.Sequence, x.Hash })
            .FirstOrDefaultAsync(ct);

        var sequence = last?.Sequence ?? 0;
        var prevHash = last?.Hash ?? LedgerHasher.Genesis;
        var now = LedgerHasher.TruncateToMicroseconds(at ?? clock.GetUtcNow());
        var result = new List<LedgerEntry>(entries.Count);

        foreach (var n in entries)
        {
            var entry = new LedgerEntry
            {
                AccountId = accountId,
                Sequence = ++sequence,
                Type = n.Type,
                VenueId = n.VenueId,
                CheckInId = n.CheckInId,
                DrinkType = n.DrinkType,
                Quantity = n.Quantity,
                Tier = n.Tier,
                Points = n.Points,
                ReversesEntryId = n.ReversesEntryId,
                Reason = n.Reason,
                ActorAdmin = n.ActorAdmin,
                CreatedAt = now,
                PrevHash = prevHash,
            };
            entry.Hash = LedgerHasher.Compute(entry);
            prevHash = entry.Hash;
            result.Add(entry);
        }

        db.LedgerEntries.AddRange(result);
        await db.SaveChangesAsync(ct);
        return result;
    }
}
