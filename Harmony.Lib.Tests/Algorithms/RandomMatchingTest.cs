namespace Harmony.Lib.Tests;

using Harmony.Lib.Algorithms;
using Harmony.Lib.Models;

public class RandomMatchingTest
{
    // RandomMatching built its CpSolver with no parameters at all, so a solve had no
    // wall-clock bound while PowermatchHighLow was capped at 19s. The random strategy is
    // caller-selected ("strategy": "random") on a Function URL with --auth-type NONE, so
    // an unbounded solve was reachable from outside: it would burn the full 40s Lambda
    // timeout and return nothing rather than degrading to a feasible pairing.
    //
    // Both algorithms now build their solver through SolverDefaults, so asserting on the
    // factory is what actually guarantees the bound -- a wall-clock assertion would not,
    // since these models solve in well under a second either way.
    [Fact]
    public void EverySolverIsTimeBounded()
    {
        var solver = SolverDefaults.CreateSolver();

        Assert.Equal($"max_time_in_seconds:{SolverDefaults.MaxTimeInSeconds}", solver.StringParameters);
        Assert.True(
            SolverDefaults.MaxTimeInSeconds + SolverDefaults.RequiredHeadroomSeconds
                <= SolverDefaults.LambdaTimeoutSeconds,
            "Solver bound must leave headroom under the deployed Lambda --timeout.");
    }

    [Fact]
    public void ProducesOneMatchupPerTeamPair()
    {
        var teams = BuildTeams(60);

        var round = new Round { Number = 2 };
        round.RandomMatch(teams);

        Assert.Equal(30, round.Matchups.Count);
        Assert.DoesNotContain(round.Matchups, m => m.IsBye);

        var paired = round.Matchups
            .SelectMany(m => new[] { m.Aff.Name, m.Neg!.Name })
            .ToList();
        Assert.Equal(teams.Count, paired.Distinct().Count());
    }

    [Fact]
    public void OddTeamCountProducesExactlyOneBye()
    {
        var teams = BuildTeams(41);

        var round = new Round { Number = 2 };
        round.RandomMatch(teams);

        Assert.Single(round.Matchups, m => m.IsBye);
        Assert.Equal(21, round.Matchups.Count);
    }

    private static List<Team> BuildTeams(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new Team { Name = $"Team {i}", Seed = i })
            .ToList();
}
