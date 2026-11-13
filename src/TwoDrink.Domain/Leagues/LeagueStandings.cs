namespace TwoDrink.Domain.Leagues;

/// <summary>A member's score in a league window: ledger points and distinct venues with a check-in.</summary>
public record MemberScore(Guid UserId, decimal Points, int DistinctVenues);

/// <summary>League ranking: points, then distinct venues (both descending). Equal on both shares a rank.</summary>
public static class LeagueStandings
{
    /// <summary>
    /// Competition ranking (1, 1, 3). Equal scores keep their input order (the sort is stable), so callers
    /// control the display order of ties.
    /// </summary>
    public static IReadOnlyList<(MemberScore Score, int Rank)> Rank(IEnumerable<MemberScore> scores)
    {
        var ordered = scores.OrderByDescending(s => s.Points).ThenByDescending(s => s.DistinctVenues).ToList();
        var result = new List<(MemberScore, int)>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var tied = i > 0 && ordered[i].Points == ordered[i - 1].Points && ordered[i].DistinctVenues == ordered[i - 1].DistinctVenues;
            result.Add((ordered[i], tied ? result[i - 1].Item2 : i + 1));
        }
        return result;
    }

    /// <summary>Everyone ranked first; empty when the top total is not above zero.</summary>
    public static IReadOnlyList<Guid> Champions(IEnumerable<MemberScore> scores)
    {
        var ranked = Rank(scores);
        if (ranked.Count == 0 || ranked[0].Score.Points <= 0) return [];
        return ranked.Where(r => r.Rank == 1).Select(r => r.Score.UserId).ToList();
    }
}
