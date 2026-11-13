using Microsoft.EntityFrameworkCore;
using Npgsql;
using TwoDrink.Api.Tests.Infrastructure;
using TwoDrink.Domain.Scoring;
using TwoDrink.Infrastructure.Data;
using TwoDrink.Infrastructure.Data.Entities;
using TwoDrink.Infrastructure.Ledger;

namespace TwoDrink.Api.Tests;

public class LedgerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static NewEntry Drink(DrinkType t, int qty) =>
        new(LedgerEntryType.Drink, t is DrinkType.Water ? 1.5m * qty : 1m * qty, DrinkType: t, Quantity: qty);

    private static async Task<Guid> NewAccount(AppDbContext db)
    {
        var id = Guid.NewGuid();
        db.LedgerAccounts.Add(new LedgerAccount { Id = id });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<IReadOnlyList<LedgerEntry>> Append(AppDbContext db, Guid account, params NewEntry[] entries)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var result = await new LedgerWriter(db, factory.Clock).AppendAsync(account, entries, default);
        await tx.CommitAsync();
        return result;
    }

    private async Task AppendInNewScope(Guid account, params NewEntry[] entries)
    {
        await using var db = factory.NewDb();
        await Append(db, account, entries);
    }

    [Fact]
    public async Task Append_chains_hashes_per_account()
    {
        await using var db = factory.NewDb();
        var acc = await NewAccount(db);
        var e = await Append(db, acc, Drink(DrinkType.Beer, 2), Drink(DrinkType.Water, 1));
        Assert.Equal([1L, 2L], e.Select(x => x.Sequence));
        Assert.Equal("GENESIS", e[0].PrevHash);
        Assert.Equal(e[0].Hash, e[1].PrevHash);
        Assert.DoesNotContain(acc, await new LedgerVerifier(db).FindBrokenChainsAsync(default)); // shared DB: other tests tamper on purpose
    }

    [Fact]
    public async Task Append_without_transaction_throws()
    {
        await using var db = factory.NewDb();
        var acc = await NewAccount(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LedgerWriter(db, factory.Clock).AppendAsync(acc, [Drink(DrinkType.Beer, 1)], default));
    }

    [Fact]
    public async Task Hash_survives_database_round_trip()
    {
        await using var db = factory.NewDb();
        var acc = await NewAccount(db);
        factory.Clock.SetUtcNow(new DateTimeOffset(2027, 2, 6, 23, 0, 0, TimeSpan.Zero).AddTicks(1234567));
        var written = (await Append(db, acc, Drink(DrinkType.Beer, 1)))[0];

        await using var fresh = factory.NewDb();
        var read = await fresh.LedgerEntries.AsNoTracking().SingleAsync(x => x.Id == written.Id);
        Assert.Equal(written.CreatedAt, read.CreatedAt);
        Assert.Equal(written.Hash, LedgerHasher.Compute(read));
        Assert.Equal(1m, read.Points);
    }

    [Fact]
    public async Task Update_is_rejected_by_database_trigger()
    {
        await using var db = factory.NewDb();
        var e = (await Append(db, await NewAccount(db), Drink(DrinkType.Beer, 1)))[0];

        // Even the owner (who has the privilege) is stopped by the trigger.
        await using var owner = factory.NewOwnerDb();
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            owner.Database.ExecuteSqlAsync($"UPDATE ledger_entries SET points = 50 WHERE id = {e.Id}"));
        Assert.Equal("P0001", ex.SqlState);
    }

    [Fact]
    public async Task Delete_and_truncate_are_rejected_for_owner_by_trigger()
    {
        await using var db = factory.NewDb();
        var e = (await Append(db, await NewAccount(db), Drink(DrinkType.Beer, 1)))[0];

        await using var owner = factory.NewOwnerDb();
        var del = await Assert.ThrowsAsync<PostgresException>(() =>
            owner.Database.ExecuteSqlAsync($"DELETE FROM ledger_entries WHERE id = {e.Id}"));
        Assert.Equal("P0001", del.SqlState);
        var trunc = await Assert.ThrowsAsync<PostgresException>(() =>
            owner.Database.ExecuteSqlRawAsync("TRUNCATE ledger_entries"));
        Assert.Equal("P0001", trunc.SqlState);
    }

    [Fact]
    public async Task Delete_is_rejected_for_app_role()
    {
        await using var db = factory.NewDb(); // connects as twodrink_api, member of twodrink_app
        var e = (await Append(db, await NewAccount(db), Drink(DrinkType.Beer, 1)))[0];

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"DELETE FROM ledger_entries WHERE id = {e.Id}"));
        Assert.Equal("42501", ex.SqlState);
        var up = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE ledger_entries SET points = 50 WHERE id = {e.Id}"));
        Assert.Equal("42501", up.SqlState);
        var tr = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("TRUNCATE ledger_entries"));
        Assert.Equal("42501", tr.SqlState);
    }

    [Fact]
    public async Task Verifier_detects_tampering()
    {
        await using var db = factory.NewDb();
        var good = await NewAccount(db);
        var bad = await NewAccount(db);
        await Append(db, good, Drink(DrinkType.Beer, 1), Drink(DrinkType.Beer, 1));
        var badEntries = await Append(db, bad, Drink(DrinkType.Beer, 1), Drink(DrinkType.Beer, 1));

        // Superuser disables the trigger, alters points and re-enables it: only the hash chain can reveal it.
        await using var owner = factory.NewOwnerDb();
        await owner.Database.ExecuteSqlRawAsync("ALTER TABLE ledger_entries DISABLE TRIGGER USER");
        try
        {
            await owner.Database.ExecuteSqlAsync($"UPDATE ledger_entries SET points = 99 WHERE id = {badEntries[0].Id}");
        }
        finally
        {
            await owner.Database.ExecuteSqlRawAsync("ALTER TABLE ledger_entries ENABLE TRIGGER USER");
        }

        var broken = await new LedgerVerifier(db).FindBrokenChainsAsync(default);
        Assert.Contains(bad, broken);
        Assert.DoesNotContain(good, broken);
    }

    [Fact]
    public async Task Verifier_detects_sequence_gap()
    {
        await using var db = factory.NewDb();
        var acc = await NewAccount(db);
        var e = await Append(db, acc, Drink(DrinkType.Beer, 1), Drink(DrinkType.Beer, 1));

        await using var owner = factory.NewOwnerDb();
        await owner.Database.ExecuteSqlRawAsync("ALTER TABLE ledger_entries DISABLE TRIGGER USER");
        try
        {
            await owner.Database.ExecuteSqlAsync($"DELETE FROM ledger_entries WHERE id = {e[0].Id}");
        }
        finally
        {
            await owner.Database.ExecuteSqlRawAsync("ALTER TABLE ledger_entries ENABLE TRIGGER USER");
        }

        Assert.Contains(acc, await new LedgerVerifier(db).FindBrokenChainsAsync(default));
    }

    [Fact]
    public async Task Concurrent_appends_do_not_fork_chain()
    {
        await using var db = factory.NewDb();
        var acc = await NewAccount(db);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => AppendInNewScope(acc, Drink(DrinkType.Beer, 1))));
        var seqs = await db.LedgerEntries.Where(x => x.AccountId == acc).Select(x => x.Sequence).OrderBy(x => x).ToListAsync();
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (long)i), seqs);
        Assert.DoesNotContain(acc, await new LedgerVerifier(db).FindBrokenChainsAsync(default)); // shared DB: other tests tamper on purpose
    }

    [Fact]
    public async Task Append_stamps_the_business_time_given_by_the_caller()
    {
        await using var db = factory.NewDb();
        var acc = await NewAccount(db);
        factory.Clock.SetUtcNow(TestClock.DefaultStart);
        var at = TestClock.DefaultStart.AddMinutes(-5).AddTicks(1234567);

        await using var tx = await db.Database.BeginTransactionAsync();
        var written = (await new LedgerWriter(db, factory.Clock).AppendAsync(acc, [Drink(DrinkType.Beer, 1)], default, at))[0];
        await tx.CommitAsync();

        Assert.Equal(LedgerHasher.TruncateToMicroseconds(at), written.CreatedAt);
        await using var fresh = factory.NewDb();
        var read = await fresh.LedgerEntries.AsNoTracking().SingleAsync(x => x.Id == written.Id);
        Assert.Equal(written.CreatedAt, read.CreatedAt);
        Assert.Equal(written.Hash, LedgerHasher.Compute(read));
    }

    [Fact]
    public async Task Ledger_has_partial_indexes_for_check_in_and_reversal_lookups()
    {
        await using var owner = factory.NewOwnerDb();
        var defs = await owner.Database
            .SqlQueryRaw<string>("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'ledger_entries'")
            .ToListAsync();
        Assert.Contains(defs, d => d.Contains("(check_in_id)") && d.Contains("WHERE (check_in_id IS NOT NULL)"));
        Assert.Contains(defs, d => d.Contains("(reverses_entry_id)") && d.Contains("WHERE (reverses_entry_id IS NOT NULL)"));
    }
}
