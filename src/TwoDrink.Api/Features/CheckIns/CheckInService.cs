using Microsoft.EntityFrameworkCore;
using TwoDrink.Api.Features.Seasons;
using TwoDrink.Api.Jobs;
using TwoDrink.Api.Features.Venues;
using TwoDrink.Domain.Badges;
using TwoDrink.Domain.Errors;
using TwoDrink.Domain.Presence;
using TwoDrink.Domain.Rules;
using TwoDrink.Domain.Scoring;
using TwoDrink.Infrastructure.Data;
using TwoDrink.Infrastructure.Data.Entities;
using TwoDrink.Infrastructure.Ledger;
using TwoDrink.Infrastructure.Storage;

namespace TwoDrink.Api.Features.CheckIns;

/// <summary>A drink line as sent by the app; <see cref="Type"/> is a <see cref="DrinkType"/> name (case-insensitive).</summary>
public record DrinkLineRequest(string? Type, int Quantity);

public record ConfirmCheckInRequest(double Lat, double Lng, double AccuracyM, bool IsMocked, IReadOnlyList<DrinkLineRequest>? Drinks);

public record BadgeAwarded(string Tier, string? ArtUrl, decimal Points);

public record ConfirmCheckInResponse(Guid CheckInId, decimal PointsAwarded, BadgeAwarded? Badge);

public record ComplementRequest(double Lat, double Lng, double AccuracyM, bool IsMocked, IReadOnlyList<DrinkLineRequest>? Drinks);

public record ComplementResponse(decimal PointsAwarded, int DrinkCount);

/// <summary>The check-in does not exist or belongs to someone else (404 <c>checkin_not_found</c>).</summary>
public sealed class CheckInNotFoundException() : Exception("Check-in not found.");

/// <summary>The intent does not exist or belongs to someone else (404 <c>intent_not_found</c>).</summary>
public sealed class CheckInIntentNotFoundException() : Exception("Check-in intent not found.");

public class CheckInService(
    AppDbContext db,
    LedgerWriter ledger,
    Geofence geofence,
    CheckInCooldown cooldown,
    DrinkCatalog catalog,
    VenueTierService tiers,
    SeasonService seasons,
    IPhotoStorage storage,
    HydrationReconciler hydration,
    CheckInCloser closer,
    RulesOptions rules,
    TimeProvider time,
    ILogger<CheckInService> log)
{
    public const long MaxPhotoBytes = 5 * 1024 * 1024;
    public const string PhotoContentType = "image/jpeg";

    /// <summary>Marks ledger entries written by a complement, so a confirm replay can tell them from its own.</summary>
    public const string ComplementReason = "complement";

    /// <summary>
    /// Turns a validated intent + uploaded selfie into a check-in, its ledger entries and (maybe) a badge — exactly once.
    /// Runs in one transaction serialised per user; a repeated call for a confirmed intent returns the stored result.
    /// </summary>
    public async Task<ConfirmCheckInResponse> ConfirmAsync(Guid userId, Guid intentId, ConfirmCheckInRequest req, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // One confirmation at a time per user: cooldowns, badge evolution and idempotency are decided under this lock.
        var userKey = userId.ToString("D");
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLocks.CheckIns}, hashtext({userKey}))", ct);

        // 1. the intent exists and is the caller's
        var intent = (await db.CheckInIntents
            .FromSql($"SELECT * FROM check_in_intents WHERE id = {intentId} FOR UPDATE")
            .ToListAsync(ct)).SingleOrDefault();
        if (intent is null || intent.UserId != userId)
            throw new CheckInIntentNotFoundException();

        // 2. already confirmed: same answer (even after the intent expired, so a late network retry is safe)
        if (intent.ConfirmedCheckInId is { } confirmedId)
            return await StoredResultAsync(userId, confirmedId, ct);

        var now = time.GetUtcNow();
        if (now >= intent.ExpiresAt)
            throw new RuleViolation("intent_expired", "The check-in intent has expired.");

        // 3. presence (against the venue position already refreshed when the intent was created)
        var venue = await db.Venues.AsNoTracking().SingleAsync(v => v.Id == intent.VenueId, ct);
        geofence.EnsureInside(new DeviceFix(new GeoPoint(req.Lat, req.Lng), req.AccuracyM, req.IsMocked),
            new GeoPoint(venue.Lat, venue.Lng));

        // 4. cooldowns (the check-in being created is not in the table yet)
        var history = db.CheckIns.AsNoTracking().Where(c => c.UserId == userId);
        var lastAnywhere = await history.MaxAsync(c => (DateTimeOffset?)c.CreatedAt, ct);
        var lastHere = await history.Where(c => c.VenueId == venue.Id).MaxAsync(c => (DateTimeOffset?)c.CreatedAt, ct);
        cooldown.Ensure(now, lastHere, lastAnywhere);

        // 5. drinks
        var lines = ValidateDrinks(req.Drinks);

        // 6. photo: freeze it first (copy out of reach of the upload SAS), then validate exactly what was frozen
        var checkInId = Guid.NewGuid();
        var finalBlob = $"final/{checkInId}.jpg";
        var photo = await storage.CopyAsync(intent.BlobName, finalBlob, ct)
                    ?? throw new RuleViolation("photo_missing", "The selfie was not uploaded.");
        ConfirmCheckInResponse result;
        try
        {
            if (!string.Equals(photo.ContentType, PhotoContentType, StringComparison.OrdinalIgnoreCase) || photo.SizeBytes > MaxPhotoBytes)
                throw new RuleViolation("photo_invalid", "The selfie must be a JPEG of at most 5 MB.");

            // 7–8. check-in, badge and ledger entries, committed together
            result = await WriteAsync(userId, intent, venue.Id, checkInId, finalBlob, lines, now, ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await DeleteBestEffortAsync(finalBlob); // nothing references the frozen copy
            throw;
        }

        await DeleteBestEffortAsync(intent.BlobName); // the upload slot is no longer needed
        return result;
    }

    /// <summary>
    /// Adds drinks to an open check-in (no new selfie). Same per-user lock as confirm plus a row lock on the check-in,
    /// so the 15-drink limit and the hydration balance hold under concurrency.
    /// </summary>
    public async Task<ComplementResponse> ComplementAsync(Guid userId, Guid checkInId, ComplementRequest req, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var userKey = userId.ToString("D");
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLocks.CheckIns}, hashtext({userKey}))", ct);

        var checkIn = (await db.CheckIns
            .FromSql($"SELECT * FROM check_ins WHERE id = {checkInId} AND user_id = {userId} FOR UPDATE")
            .ToListAsync(ct)).SingleOrDefault();
        if (checkIn is null || checkIn.UserId != userId)
            throw new CheckInNotFoundException();

        var now = time.GetUtcNow();
        if (checkIn.ClosedAt is not null || now >= checkIn.ComplementsUntil)
            throw new RuleViolation("checkin_closed", "This check-in no longer accepts drinks.");

        var venue = await db.Venues.AsNoTracking().SingleAsync(v => v.Id == checkIn.VenueId, ct);
        geofence.EnsureInside(new DeviceFix(new GeoPoint(req.Lat, req.Lng), req.AccuracyM, req.IsMocked),
            new GeoPoint(venue.Lat, venue.Lng));

        var lines = ValidateDrinks(req.Drinks, checkIn.DrinkCount);

        var entries = lines.Select(l => new NewEntry(LedgerEntryType.Drink, catalog.PointsFor(l),
            VenueId: venue.Id, CheckInId: checkIn.Id, DrinkType: l.Type, Quantity: l.Quantity, Reason: ComplementReason)).ToList();
        checkIn.DrinkCount += lines.Sum(l => l.Quantity);
        await ledger.AppendAsync(checkIn.LedgerAccountId, entries, ct, now); // also saves the new DrinkCount
        var hydrationPoints = await hydration.ReconcileAsync(checkIn, ct, ComplementReason, now);

        await tx.CommitAsync(ct);
        return new ComplementResponse(entries.Sum(e => e.Points) + hydrationPoints, checkIn.DrinkCount);
    }

    private async Task<ConfirmCheckInResponse> WriteAsync(Guid userId, CheckInIntent intent, Guid venueId, Guid checkInId,
        string photoBlob, IReadOnlyList<DrinkLine> lines, DateTimeOffset now, CancellationToken ct)
    {
        var ledgerAccountId = await db.Users.Where(u => u.Id == userId).Select(u => u.LedgerAccountId).SingleAsync(ct);
        var season = await seasons.AtAsync(now, ct);

        // 7. badge: tier and art in force now; no season → no badge (drinks still score)
        UserBadge? badge = null;
        if (season is not null)
        {
            var (tier, artUrl) = await tiers.CurrentAsync(venueId, now, ct);
            // Tier is stored as its name, so SQL MAX would compare alphabetically: take the max over the enum in memory.
            var heldTiers = await db.UserBadges.AsNoTracking()
                .Where(b => b.UserId == userId && b.VenueId == venueId && b.SeasonId == season.Id)
                .Select(b => b.Tier)
                .ToListAsync(ct);
            Tier? held = heldTiers.Count == 0 ? null : heldTiers.Max();
            if (BadgeEligibility.ShouldGrant(tier, held))
                badge = new UserBadge
                {
                    Id = Guid.NewGuid(), UserId = userId, VenueId = venueId, SeasonId = season.Id, Tier = tier,
                    ArtUrl = artUrl, CheckInId = checkInId, GrantedAt = now,
                };
        }

        // A new check-in ends the complement window of the previous open one (we hold the per-user lock).
        var previousOpen = await db.CheckIns.AsNoTracking()
            .Where(c => c.UserId == userId && c.ClosedAt == null).Select(c => c.Id).ToListAsync(ct);
        foreach (var id in previousOpen)
            await closer.CloseAsync(id, ct);

        var checkIn = new CheckIn
        {
            Id = checkInId, UserId = userId, LedgerAccountId = ledgerAccountId, VenueId = venueId, SeasonId = season?.Id,
            PhotoBlobName = photoBlob, CreatedAt = now, ComplementsUntil = now + rules.ComplementWindow,
            DrinkCount = lines.Sum(l => l.Quantity),
        };
        db.CheckIns.Add(checkIn);
        intent.ConfirmedCheckInId = checkInId;
        if (badge is not null) db.UserBadges.Add(badge);

        var entries = new List<NewEntry> { new(LedgerEntryType.CheckIn, 0m, VenueId: venueId, CheckInId: checkInId) };
        entries.AddRange(lines.Select(l => new NewEntry(LedgerEntryType.Drink, catalog.PointsFor(l),
            VenueId: venueId, CheckInId: checkInId, DrinkType: l.Type, Quantity: l.Quantity)));
        if (badge is not null)
            entries.Add(new NewEntry(LedgerEntryType.Badge, TierPoints.Of(badge.Tier),
                VenueId: venueId, CheckInId: checkInId, Tier: badge.Tier));

        // Saves the check-in, intent and badge together with the entries (same transaction).
        await ledger.AppendAsync(ledgerAccountId, entries, ct, now);
        var hydrationPoints = await hydration.ReconcileAsync(checkIn, ct, at: now);

        return new ConfirmCheckInResponse(checkInId, entries.Sum(e => e.Points) + hydrationPoints,
            badge is null ? null : new BadgeAwarded(badge.Tier.ToString(), badge.ArtUrl, TierPoints.Of(badge.Tier)));
    }

    /// <summary>The result of an already-confirmed intent, rebuilt from the ledger and the badge row.</summary>
    private async Task<ConfirmCheckInResponse> StoredResultAsync(Guid userId, Guid checkInId, CancellationToken ct)
    {
        // Scoped to the account so the lookups stay on the ledger indexes.
        var accountId = await db.Users.Where(u => u.Id == userId).Select(u => u.LedgerAccountId).SingleAsync(ct);
        var mine = db.LedgerEntries.Where(e => e.AccountId == accountId && e.CheckInId == checkInId);
        // Only what the confirm itself wrote: complements (and the hydration swings they cause) are not part of it.
        var points = await mine
            .Where(e => e.Type != LedgerEntryType.Reversal && e.Reason != ComplementReason)
            .SumAsync(e => e.Points, ct);
        var badge = await db.UserBadges.AsNoTracking().SingleOrDefaultAsync(b => b.CheckInId == checkInId, ct);
        decimal badgePoints = 0;
        if (badge is not null)
            badgePoints = await mine
                .Where(e => e.Type == LedgerEntryType.Badge)
                .Select(e => e.Points).FirstOrDefaultAsync(ct);
        return new ConfirmCheckInResponse(checkInId, points,
            badge is null ? null : new BadgeAwarded(badge.Tier.ToString(), badge.ArtUrl, badgePoints));
    }

    /// <summary>Parses and merges the lines (same type summed); 1..MaxDrinksPerCheckIn in total, each quantity ≥ 1.</summary>
    private IReadOnlyList<DrinkLine> ValidateDrinks(IReadOnlyList<DrinkLineRequest>? drinks, int alreadyHad = 0)
    {
        if (drinks is null || drinks.Count == 0)
            throw new RuleViolation("drinks_required", "At least one drink is required.");

        var merged = new Dictionary<DrinkType, int>();
        var total = 0L;
        foreach (var d in drinks)
        {
            if (d is null || !TryParseDrinkType(d.Type, out var type))
                throw new RuleViolation("invalid_drinks", "Unknown drink type.");
            if (d.Quantity < 1)
                throw new RuleViolation("drinks_required", "Each drink quantity must be at least 1.");
            total += d.Quantity;
            merged[type] = merged.GetValueOrDefault(type) + d.Quantity;
        }
        if (alreadyHad + total > rules.MaxDrinksPerCheckIn)
            throw new RuleViolation("drink_limit", $"At most {rules.MaxDrinksPerCheckIn} drinks per check-in.");

        return merged.Select(kv => new DrinkLine(kv.Key, kv.Value)).ToList();
    }

    /// <summary>Only names are accepted (numbers such as "1" are rejected even if they map to an enum value).</summary>
    private static bool TryParseDrinkType(string? s, out DrinkType type)
    {
        foreach (var t in Enum.GetValues<DrinkType>())
        {
            if (string.Equals(t.ToString(), s, StringComparison.OrdinalIgnoreCase))
            {
                type = t;
                return true;
            }
        }
        type = default;
        return false;
    }

    private async Task DeleteBestEffortAsync(string blobName)
    {
        try
        {
            await storage.DeleteAsync(blobName, CancellationToken.None);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Could not delete selfie blob {Blob}", blobName);
        }
    }
}
