namespace Harmony.Lib.Tests;

using System.Diagnostics;
using Harmony.Lib.Algorithms;
using Harmony.Lib.Models;

public class RandomMatchingTest
{
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

    // Sides are assigned after the matching (one edge per unordered pair), so the side
    // rules, rematch rule and bye eligibility have to be checked on the output rather
    // than trusted from the model. EspressoR6 has 29 of 31 teams locked to one side and
    // a full opponent history.
    [Fact]
    public void RespectsSideHistoryAndByeEligibility()
    {
        var (teams, roundNumber) = PowermatchTest.LoadScenario("EspressoR6.json");

        var round = new Round { Number = roundNumber };
        round.RandomMatch(teams);

        Assert.Equal(16, round.Matchups.Count);
        Assert.Equal(teams.Count, round.Matchups.SelectMany(m => new[] { m.Aff, m.Neg }).OfType<Team>().Distinct().Count());
        foreach (var m in round.Matchups.Where(m => !m.IsBye))
        {
            Assert.True(m.Aff.CanGoAff, $"{m.Aff.Name} cannot go aff");
            Assert.True(m.Neg!.CanGoNeg, $"{m.Neg.Name} cannot go neg");
            Assert.False(m.Aff.HasHit(m.Neg), $"{m.Aff.Name} already debated {m.Neg.Name}");
        }
        var bye = Assert.Single(round.Matchups, m => m.IsBye);
        Assert.False(bye.Aff.HadBye);
    }

    [Fact]
    public void OneSidedOpponentHistoryStillCountsAsARematch()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var teams = BuildTeams(4);
            // Only Team 1 records the meeting with Team 2.
            teams[0].RecordOpponent(teams[1]);

            var edges = new RandomMatching(new Random(seed)).SolveMatching(teams);

            Assert.DoesNotContain(edges, e => new[] { e.Aff.Name, e.Neg!.Name }.Order().SequenceEqual(["Team 1", "Team 2"]));
        }
    }

    [Fact]
    public void DifferentSeedsProduceDifferentPairings()
    {
        static string Pair(Random random)
        {
            var teams = BuildTeams(20);
            var edges = new RandomMatching(random).SolveMatching(teams);
            return string.Join("|", edges.Select(e => e.ToString()).OrderBy(s => s));
        }

        // 20 teams admit ~6.5e8 matchings; two seeds colliding would be a broken RNG path.
        Assert.NotEqual(Pair(new Random(1)), Pair(new Random(2)));
    }

    [Fact]
    public void SameSeedIsReproducible()
    {
        static string Pair(Random random)
        {
            var teams = BuildTeams(21);
            var edges = new RandomMatching(random).SolveMatching(teams);
            return string.Join("|", edges.Select(e => e.ToString()).OrderBy(s => s));
        }

        Assert.Equal(Pair(new Random(5)), Pair(new Random(5)));
    }

    // With a random cost per edge, the "random" pairing is a min-cost matching over
    // unstructured weights. CP-SAT ran a 202-team round 1 of that to its time limit;
    // the exact matching algorithm takes well under a second.
    [Fact]
    public void LargeRound1PairsInstantly()
    {
        var teams = BuildTeams(202);

        var sw = Stopwatch.StartNew();
        var round = new Round { Number = 1 };
        round.RandomMatch(teams);
        sw.Stop();

        Assert.Equal(101, round.Matchups.Count);
        Assert.True(sw.Elapsed.TotalSeconds < 5, $"Random pairing of 202 teams took {sw.Elapsed.TotalSeconds:F1}s");
    }

    private static List<Team> BuildTeams(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new Team { Name = $"Team {i}", Seed = i })
            .ToList();
}
