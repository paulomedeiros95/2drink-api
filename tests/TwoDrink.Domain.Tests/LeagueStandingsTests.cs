using TwoDrink.Domain.Leagues;

namespace TwoDrink.Domain.Tests;

public class LeagueStandingsTests
{
    static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

    [Fact]
    public void Tie_on_points_broken_by_distinct_venues()
    {
        var r = LeagueStandings.Rank([new(A, 10, 2), new(B, 10, 3)]);
        Assert.Equal(B, r[0].Score.UserId);
        Assert.Equal(1, r[0].Rank);
        Assert.Equal(2, r[1].Rank);
    }

    [Fact]
    public void Full_tie_gives_multiple_champions() =>
        Assert.Equal(2, LeagueStandings.Champions([new(A, 10, 3), new(B, 10, 3), new(C, 4, 1)]).Count);

    [Fact]
    public void No_champion_when_nobody_scored() =>
        Assert.Empty(LeagueStandings.Champions([new(A, 0, 0)]));

    [Fact]
    public void No_champion_when_empty_or_negative()
    {
        Assert.Empty(LeagueStandings.Champions([]));
        Assert.Empty(LeagueStandings.Champions([new(A, -1, 0)]));
    }

    [Fact]
    public void Ties_share_a_rank_and_the_next_rank_skips()
    {
        var r = LeagueStandings.Rank([new(A, 5, 1), new(B, 10, 2), new(C, 10, 2), new(D, 1, 0)]);
        Assert.Equal([1, 1, 3, 4], r.Select(x => x.Rank));
        Assert.Equal([B, C, A, D], r.Select(x => x.Score.UserId));
    }

    [Fact]
    public void Equal_scores_keep_input_order()
    {
        var r = LeagueStandings.Rank([new(C, 3, 1), new(A, 3, 1), new(B, 3, 1)]);
        Assert.Equal([C, A, B], r.Select(x => x.Score.UserId));
    }
}
