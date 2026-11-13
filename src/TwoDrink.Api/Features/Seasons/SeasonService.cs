using Microsoft.EntityFrameworkCore;
using Npgsql;
using TwoDrink.Domain.Errors;
using TwoDrink.Domain.Rules;
using TwoDrink.Infrastructure.Data;
using TwoDrink.Infrastructure.Data.Entities;

namespace TwoDrink.Api.Features.Seasons;

public class SeasonService(AppDbContext db)
{
    /// <summary>The season containing the Sao Paulo calendar date of <paramref name="at"/>, or null.</summary>
    public async Task<Season?> AtAsync(DateTimeOffset at, CancellationToken ct)
    {
        var day = BusinessTime.Today(at);
        return await db.Seasons.AsNoTracking().FirstOrDefaultAsync(s => s.StartDate <= day && day <= s.EndDate, ct);
    }

    public async Task<Season> CreateAsync(string name, DateOnly start, DateOnly end, CancellationToken ct)
    {
        if (start > end)
            throw new RuleViolation("season_dates_invalid", "Season start must not be after its end.");
        if (await db.Seasons.AnyAsync(s => s.StartDate <= end && start <= s.EndDate, ct))
            throw new RuleViolation("season_overlap", "Season overlaps an existing season.");

        var season = new Season { Id = Guid.NewGuid(), Name = name, StartDate = start, EndDate = end };
        db.Seasons.Add(season);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
        {
            throw new RuleViolation("season_overlap", "Season overlaps an existing season.");
        }
        return season;
    }
}
