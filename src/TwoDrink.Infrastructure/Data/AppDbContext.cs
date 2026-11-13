using Microsoft.EntityFrameworkCore;
using TwoDrink.Infrastructure.Data.Entities;

namespace TwoDrink.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Subject).HasMaxLength(255);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.DisplayName).HasMaxLength(40);
            e.Property(x => x.AcceptedTermsVersion).HasMaxLength(32);
            e.Property(x => x.Locale).HasMaxLength(8);
            e.Ignore(x => x.ProfileComplete);
            e.HasIndex(x => new { x.Provider, x.Subject }).IsUnique();
            e.HasIndex(x => x.LedgerAccountId).IsUnique();
            e.HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.LedgerAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

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
