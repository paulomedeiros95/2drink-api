using Microsoft.EntityFrameworkCore;
using TwoDrink.Infrastructure.Data.Entities;

namespace TwoDrink.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<VenueSponsorship> VenueSponsorships => Set<VenueSponsorship>();
    public DbSet<Season> Seasons => Set<Season>();

    public DbSet<CheckIn> CheckIns => Set<CheckIn>();
    public DbSet<CheckInIntent> CheckInIntents => Set<CheckInIntent>();
    public DbSet<UserBadge> UserBadges => Set<UserBadge>();

    public DbSet<League> Leagues => Set<League>();
    public DbSet<LeagueMembership> LeagueMemberships => Set<LeagueMembership>();
    public DbSet<LeagueInvite> LeagueInvites => Set<LeagueInvite>();
    public DbSet<Trophy> Trophies => Set<Trophy>();

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
            e.HasIndex(x => new { x.AccountId, x.CreatedAt });
            e.HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Venue>(e =>
        {
            e.ToTable("venues");
            e.Property(x => x.PlaceId).HasMaxLength(255);
            e.Property(x => x.Name).HasMaxLength(255);
            e.Property(x => x.Address).HasMaxLength(500);
            e.HasIndex(x => x.PlaceId).IsUnique();
        });

        b.Entity<VenueSponsorship>(e =>
        {
            e.ToTable("venue_sponsorships");
            e.Property(x => x.Tier).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.BadgeArtUrl).HasMaxLength(2048);
            e.Property(x => x.CreatedBy).HasMaxLength(255);
            e.HasIndex(x => new { x.VenueId, x.StartsAt });
            e.HasOne<Venue>().WithMany().HasForeignKey(x => x.VenueId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CheckIn>(e =>
        {
            e.ToTable("check_ins");
            e.Property(x => x.PhotoBlobName).HasMaxLength(200);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.UserId, x.VenueId, x.CreatedAt });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.LedgerAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Venue>().WithMany().HasForeignKey(x => x.VenueId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Season>().WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CheckInIntent>(e =>
        {
            e.ToTable("check_in_intents");
            e.Property(x => x.BlobName).HasMaxLength(200);
            e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Venue>().WithMany().HasForeignKey(x => x.VenueId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<UserBadge>(e =>
        {
            e.ToTable("user_badges");
            e.Property(x => x.Tier).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ArtUrl).HasMaxLength(2048);
            e.HasIndex(x => new { x.UserId, x.VenueId, x.SeasonId, x.Tier }).IsUnique();
            e.HasIndex(x => x.CheckInId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Venue>().WithMany().HasForeignKey(x => x.VenueId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Season>().WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CheckIn>().WithMany().HasForeignKey(x => x.CheckInId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<League>(e =>
        {
            e.ToTable("leagues");
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Description).HasMaxLength(280);
            e.Property(x => x.CoverBlobName).HasMaxLength(200);
            e.HasIndex(x => x.CreatorId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Season>().WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<LeagueMembership>(e =>
        {
            e.ToTable("league_memberships");
            e.HasKey(x => new { x.LeagueId, x.UserId });
            e.HasIndex(x => x.UserId);
            e.HasOne<League>().WithMany().HasForeignKey(x => x.LeagueId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<LeagueInvite>(e =>
        {
            e.ToTable("league_invites");
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.LeagueId);
            e.HasOne<League>().WithMany().HasForeignKey(x => x.LeagueId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UsedBy).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Trophy>(e =>
        {
            e.ToTable("trophies");
            e.HasIndex(x => new { x.LeagueId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<League>().WithMany().HasForeignKey(x => x.LeagueId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        var season2026H2 = new Season { Id = new Guid("5ea50000-2026-4000-8000-000000000002"), Name = "2026 – 2º semestre", StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 12, 31) };
        var season2027H1 = new Season { Id = new Guid("5ea50000-2027-4000-8000-000000000001"), Name = "2027 – 1º semestre", StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 6, 30) };
        b.Entity<Season>(e =>
        {
            e.ToTable("seasons");
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => new { x.StartDate, x.EndDate });
            e.HasData(season2026H2, season2027H1);
        });
    }
}
