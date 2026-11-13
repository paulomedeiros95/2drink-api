namespace TwoDrink.Infrastructure.Data;

/// <summary>
/// Namespaces (first key) for the two-key form <c>pg_advisory_xact_lock(namespace, hashtext(id::text))</c>,
/// so locks of different features on the same id never collide.
/// </summary>
public static class AdvisoryLocks
{
    /// <summary>Per ledger account: serialises appends so the hash chain cannot fork.</summary>
    public const int Ledger = 1;

    /// <summary>Per user: league slot accounting (reserved for leagues).</summary>
    public const int LeagueSlots = 2;

    /// <summary>Per user: check-in confirmation (cooldowns, badges, idempotency).</summary>
    public const int CheckIns = 3;
}
