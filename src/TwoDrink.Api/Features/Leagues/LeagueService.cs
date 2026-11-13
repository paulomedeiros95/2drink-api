using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TwoDrink.Domain.Errors;
using TwoDrink.Domain.Rules;
using TwoDrink.Infrastructure.Data;
using TwoDrink.Infrastructure.Data.Entities;

namespace TwoDrink.Api.Features.Leagues;

public record CreateLeagueRequest(string? Name, string? Description, DateOnly StartDate, DateOnly EndDate);

public record LeagueSummary(Guid Id, string Name, string? Description, DateOnly StartDate, DateOnly EndDate, string Status);

public record MyLeague(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, string Status, int MemberCount);

public record InviteResponse(string Token, string Url, DateTimeOffset ExpiresAt);

/// <summary>404 <c>league_not_found</c>.</summary>
public sealed class LeagueNotFoundException() : Exception("League not found.");

/// <summary>403: only the creator may invite.</summary>
public sealed class NotLeagueCreatorException() : Exception("Only the league creator can invite.");

/// <summary>404 <c>invite_invalid</c>: no invite matches the token.</summary>
public sealed class InviteInvalidException() : Exception("Invite not found.");

/// <summary>404 <c>not_member</c>.</summary>
public sealed class NotMemberException() : Exception("Not a member of this league.");

public class LeagueService(AppDbContext db, RulesOptions rules, TimeProvider time, IConfiguration config)
{
    public const int MaxNameLength = 60;
    public const int MaxDescriptionLength = 280;

    public static string Status(DateOnly today, DateOnly start, DateOnly end) =>
        today < start ? "upcoming" : today > end ? "ended" : "active";

    public async Task<LeagueSummary> CreateAsync(Guid userId, CreateLeagueRequest req, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? "";
        if (name.Length is 0 or > MaxNameLength)
            throw new RuleViolation("league_name_invalid", $"The name must have 1 to {MaxNameLength} characters.");
        var description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        if (description is { Length: > MaxDescriptionLength })
            throw new RuleViolation("league_description_invalid", $"The description must have at most {MaxDescriptionLength} characters.");

        var now = time.GetUtcNow();
        var today = BusinessTime.Today(now);
        if (req.StartDate > req.EndDate || req.EndDate < today)
            throw new RuleViolation("league_dates_invalid", "The start must not be after the end, and the end must not be in the past.");

        // Both ends must fall in ONE season.
        var all = await db.Seasons.AsNoTracking().ToListAsync(ct);
        var season = all.FirstOrDefault(s => s.StartDate <= req.StartDate && req.EndDate <= s.EndDate)
                     ?? throw new RuleViolation("dates_outside_season", "The league dates must fit within a single season.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockSlotsAsync(userId, ct);
        await EnsureFreeSlotAsync(userId, today, ct);

        var league = new League
        {
            Id = Guid.NewGuid(), SeasonId = season.Id, CreatorId = userId, Name = name, Description = description,
            StartDate = req.StartDate, EndDate = req.EndDate,
        };
        db.Leagues.Add(league);
        db.LeagueMemberships.Add(new LeagueMembership { LeagueId = league.Id, UserId = userId, JoinedAt = now });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Summarize(league, today);
    }

    public async Task<InviteResponse> CreateInviteAsync(Guid userId, Guid leagueId, CancellationToken ct)
    {
        var league = await db.Leagues.AsNoTracking().SingleOrDefaultAsync(l => l.Id == leagueId, ct)
                     ?? throw new LeagueNotFoundException();
        if (league.CreatorId != userId)
            throw new NotLeagueCreatorException();

        var now = time.GetUtcNow();
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var invite = new LeagueInvite
        {
            Id = Guid.NewGuid(), LeagueId = leagueId, TokenHash = HashToken(token), ExpiresAt = now + rules.InviteLifetime,
        };
        db.LeagueInvites.Add(invite);
        await db.SaveChangesAsync(ct);

        var baseUrl = (config["Invites:BaseUrl"] ?? throw new InvalidOperationException("Invites:BaseUrl is not configured.")).TrimEnd('/');
        return new InviteResponse(token, $"{baseUrl}/i/{token}", invite.ExpiresAt);
    }

    /// <summary>
    /// Every check and the single-use claim run in one transaction under the user's slot lock, so a failed check
    /// (e.g. <c>league_limit</c>) rolls the claim back and the invite stays usable.
    /// </summary>
    public async Task<LeagueSummary> AcceptAsync(Guid userId, string token, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var today = BusinessTime.Today(now);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockSlotsAsync(userId, ct);

        var hash = HashToken(token);
        var invite = await db.LeagueInvites.AsNoTracking().SingleOrDefaultAsync(i => i.TokenHash == hash, ct)
                     ?? throw new InviteInvalidException();
        if (now >= invite.ExpiresAt)
            throw new RuleViolation("invite_expired", "The invite has expired.");
        if (invite.UsedBy is not null)
            throw new RuleViolation("invite_used", "The invite has already been used.");

        var league = await db.Leagues.AsNoTracking().SingleAsync(l => l.Id == invite.LeagueId, ct);
        if (today > league.EndDate)
            throw new RuleViolation("league_ended", "The league has ended.");

        var membership = await db.LeagueMemberships.AsNoTracking()
            .SingleOrDefaultAsync(m => m.LeagueId == league.Id && m.UserId == userId, ct);
        if (membership?.LeftAt is not null)
            throw new RuleViolation("left_league", "You left this league and cannot join again.");
        if (membership is not null)
            throw new RuleViolation("already_member", "You are already a member of this league.");

        await EnsureFreeSlotAsync(userId, today, ct);

        // Atomic claim: a concurrent accept blocks on the row lock, then re-evaluates used_by and matches nothing.
        var claimed = await db.Database.ExecuteSqlAsync(
            $"UPDATE league_invites SET used_by = {userId}, used_at = {now} WHERE id = {invite.Id} AND used_by IS NULL AND expires_at > {now}", ct);
        if (claimed != 1)
            throw new RuleViolation("invite_used", "The invite has already been used.");

        db.LeagueMemberships.Add(new LeagueMembership { LeagueId = league.Id, UserId = userId, JoinedAt = now });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Summarize(league, today);
    }

    public async Task LeaveAsync(Guid userId, Guid leagueId, CancellationToken ct)
    {
        var membership = await db.LeagueMemberships.SingleOrDefaultAsync(m => m.LeagueId == leagueId && m.UserId == userId && m.LeftAt == null, ct)
                         ?? throw new NotMemberException();
        var league = await db.Leagues.AsNoTracking().SingleAsync(l => l.Id == leagueId, ct);
        var now = time.GetUtcNow();
        if (league.CreatorId == userId && BusinessTime.Today(now) <= league.EndDate)
            throw new RuleViolation("creator_cannot_leave", "The creator cannot leave a league that has not ended.");

        membership.LeftAt = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MyLeague>> MineAsync(Guid userId, CancellationToken ct)
    {
        var today = BusinessTime.Today(time.GetUtcNow());
        var rows = await (from m in db.LeagueMemberships.AsNoTracking()
                          join l in db.Leagues.AsNoTracking() on m.LeagueId equals l.Id
                          where m.UserId == userId && m.LeftAt == null
                          orderby l.StartDate descending, l.Name
                          select new
                          {
                              l.Id, l.Name, l.StartDate, l.EndDate,
                              MemberCount = db.LeagueMemberships.Count(x => x.LeagueId == l.Id && x.LeftAt == null),
                          }).ToListAsync(ct);
        return rows.Select(r => new MyLeague(r.Id, r.Name, r.StartDate, r.EndDate, Status(today, r.StartDate, r.EndDate), r.MemberCount)).ToList();
    }

    /// <summary>A membership occupies a slot while not left and now &lt; end+1 00:00 Sao Paulo, i.e. today &lt;= EndDate.</summary>
    async Task EnsureFreeSlotAsync(Guid userId, DateOnly today, CancellationToken ct)
    {
        var used = await (from m in db.LeagueMemberships.AsNoTracking()
                          join l in db.Leagues.AsNoTracking() on m.LeagueId equals l.Id
                          where m.UserId == userId && m.LeftAt == null && l.EndDate >= today
                          select m.LeagueId).CountAsync(ct);
        if (used >= rules.MaxActiveLeaguesPerUser)
            throw new RuleViolation("league_limit", $"You can be in at most {rules.MaxActiveLeaguesPerUser} active or upcoming leagues.");
    }

    Task LockSlotsAsync(Guid userId, CancellationToken ct)
    {
        var key = userId.ToString("D");
        return db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdvisoryLocks.LeagueSlots}, hashtext({key}))", ct);
    }

    static LeagueSummary Summarize(League l, DateOnly today) =>
        new(l.Id, l.Name, l.Description, l.StartDate, l.EndDate, Status(today, l.StartDate, l.EndDate));

    static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
