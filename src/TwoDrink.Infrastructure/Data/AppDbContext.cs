using Microsoft.EntityFrameworkCore;
using TwoDrink.Infrastructure.Data.Entities;

namespace TwoDrink.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<LedgerAccount>(e => e.ToTable("ledger_accounts"));

        b.Entity<LedgerEntry>(e =>
        {
            e.ToTable("ledger_entries");
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.DrinkType).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Tier).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Points).HasPrecision(8, 1);
            e.Property(x => x.PrevHash).HasMaxLength(64);
            e.Property(x => x.Hash).HasMaxLength(64);
            e.HasIndex(x => new { x.AccountId, x.Sequence }).IsUnique();
            e.HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
