using Microsoft.EntityFrameworkCore;
using TwoDrink.Infrastructure.Data;

namespace TwoDrink.Infrastructure.Ledger;

public class LedgerVerifier(AppDbContext db)
{
    /// <summary>Returns the accounts whose chain fails verification (bad hash, broken link or sequence gap).</summary>
    public async Task<IReadOnlyList<Guid>> FindBrokenChainsAsync(CancellationToken ct)
    {
        var broken = new List<Guid>();
        var stream = db.LedgerEntries.AsNoTracking()
            .OrderBy(x => x.AccountId).ThenBy(x => x.Sequence)
            .AsAsyncEnumerable();

        Guid? current = null;
        var isBroken = false;
        long expectedSeq = 0;
        var expectedPrev = LedgerHasher.Genesis;

        await foreach (var e in stream.WithCancellation(ct))
        {
            if (current != e.AccountId)
            {
                current = e.AccountId;
                isBroken = false;
                expectedSeq = 0;
                expectedPrev = LedgerHasher.Genesis;
            }
            if (isBroken) continue;

            if (e.Sequence != expectedSeq + 1 || e.PrevHash != expectedPrev || e.Hash != LedgerHasher.Compute(e))
            {
                isBroken = true;
                broken.Add(e.AccountId);
                continue;
            }
            expectedSeq = e.Sequence;
            expectedPrev = e.Hash;
        }
        return broken;
    }
}
