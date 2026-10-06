namespace Harmony.Lib.Tests;

using Google.OrTools.Sat;
using Harmony.Lib.Algorithms;
using Harmony.Lib.Models;

public class MinCostPerfectMatchingTest
{
    [Fact]
    public void PicksTheCheaperOfTwoPerfectMatchingsOnAFourCycle()
    {
        // 0-1 (5), 1-2 (1), 2-3 (5), 3-0 (1): {0-1, 2-3} costs 10, {1-2, 3-0} costs 2.
        var partner = MinCostPerfectMatching.Solve(4, [(0, 1, 5), (1, 2, 1), (2, 3, 5), (3, 0, 1)]);

        Assert.Equal([3, 2, 1, 0], partner!);
    }

    [Fact]
    public void PrefersAPerfectMatchingOverACheaperPartialOne()
    {
        // 0-1 is very cheap but strands 2 and 3; the only perfect matching is {0-2, 1-3}.
        var partner = MinCostPerfectMatching.Solve(4, [(0, 1, -1_000_000), (0, 2, 500_000_000), (1, 3, 500_000_000)]);

        Assert.Equal([2, 3, 0, 1], partner!);
    }

    [Fact]
    public void ReturnsNullWhenNoPerfectMatchingExists()
    {
        // A star: 0 is adjacent to everyone, nobody else is adjacent to each other.
        Assert.Null(MinCostPerfectMatching.Solve(4, [(0, 1, 1), (0, 2, 1), (0, 3, 1)]));
        Assert.Null(MinCostPerfectMatching.Solve(3, [(0, 1, 1), (1, 2, 1), (0, 2, 1)]));
        Assert.Null(MinCostPerfectMatching.Solve(2, []));
    }

    [Fact]
    public void HandlesOddCyclesThatFoolTheLinearRelaxation()
    {
        // Two triangles joined by one edge: each triangle can only match one of its own
        // pairs, so the bridge 2-3 is forced even though it is the most expensive edge.
        var partner = MinCostPerfectMatching.Solve(6,
            [(0, 1, 1), (1, 2, 1), (0, 2, 1), (3, 4, 1), (4, 5, 1), (3, 5, 1), (2, 3, 100)]);

        Assert.NotNull(partner);
        Assert.Equal(3, partner![2]);
        Assert.Equal(1, partner[0]);
        Assert.Equal(5, partner[4]);
    }

    // The blossom implementation is dense, index-heavy code; the real guarantee is
    // agreement with an independent exact solver. CP-SAT proves optimality quickly on
    // small graphs, so it serves as the oracle over many random instances: same
    // feasibility verdict, same optimal cost.
    [Fact]
    public void AgreesWithCpSatOnRandomGraphs()
    {
        var rng = new Random(20260918);
        var infeasible = 0;
        for (var iteration = 0; iteration < 150; iteration++)
        {
            var n = 2 * rng.Next(1, 11);
            var density = rng.NextDouble() * 0.8 + 0.2;
            var edges = new List<(int U, int V, long Cost)>();
            for (var i = 0; i < n; i++)
                for (var j = i + 1; j < n; j++)
                    if (rng.NextDouble() < density)
                        edges.Add((i, j, RandomCost(rng)));

            var expected = CpSatOptimum(n, edges);
            var partner = MinCostPerfectMatching.Solve(n, edges);

            if (expected == null)
            {
                Assert.Null(partner);
                infeasible++;
                continue;
            }

            Assert.NotNull(partner);
            AssertPerfectMatching(n, partner!, edges);
            Assert.Equal(expected.Value, MatchingCost(partner!, edges));
        }
        Assert.InRange(infeasible, 1, 149);
    }

    // Same check against the real cost function: random tournament states in the shape
    // production sees (win brackets, seeds, side constraints, rematch history), with the
    // whole PowermatchHighLow pipeline compared to a CP-SAT model of the same edges.
    [Fact]
    public void PowermatchAgreesWithCpSatOnRandomTournaments()
    {
        var rng = new Random(1931);
        for (var iteration = 0; iteration < 60; iteration++)
        {
            var teams = RandomTournament(rng, teamCount: rng.Next(6, 19), roundsPlayed: rng.Next(0, 5));
            var byeTeam = teams.Count % 2 == 0 ? null
                : teams.Where(t => !t.HadBye).OrderBy(t => t.Wins).ThenByDescending(t => t.Seed).FirstOrDefault();
            var pool = teams.Where(t => t != byeTeam).ToList();
            var edges = new List<(int U, int V, long Cost)>();
            for (var i = 0; i < pool.Count; i++)
                for (var j = i + 1; j < pool.Count; j++)
                    if (PairRules.IsLegal(pool[i], pool[j]))
                        edges.Add((i, j, pool[i].MatchupCost(pool[j])));
            var expected = CpSatOptimum(pool.Count, edges);

            List<PowermatchHighLow.Edge> result;
            try
            {
                result = new PowermatchHighLow().SolveMatching(teams);
            }
            catch (CannotPairException)
            {
                Assert.Null(expected);
                continue;
            }

            Assert.NotNull(expected);
            var matchups = result.Where(e => e.Neg != null).ToList();
            Assert.Equal(pool.Count / 2, matchups.Count);
            Assert.Equal(expected!.Value, matchups.Sum(e => (long)e.Aff.MatchupCost(e.Neg!)));
            foreach (var e in matchups)
            {
                Assert.True(e.Aff.CanGoAff && e.Neg!.CanGoNeg, $"{e} violates side balance");
                Assert.False(e.Aff.HasHit(e.Neg!), $"{e} is a rematch");
            }
            if (byeTeam != null) Assert.Same(byeTeam, Assert.Single(result, e => e.Neg == null).Aff);
        }
    }

    private static long RandomCost(Random rng) => rng.Next(4) switch
    {
        0 => rng.Next(-100, 100),
        1 => rng.Next(0, 10_000),
        2 => new[] { 0, 100_000, 10_000_000, 500_000_000 }[rng.Next(4)] + rng.Next(-30_000, 10_000),
        _ => rng.Next(1000),
    };

    private static long? CpSatOptimum(int n, List<(int U, int V, long Cost)> edges)
    {
        var model = new CpModel();
        var vars = edges.Select((e, k) => model.NewBoolVar($"e{k}")).ToList();
        for (var i = 0; i < n; i++)
        {
            var incident = edges.Select((e, k) => (e, k)).Where(x => x.e.U == i || x.e.V == i).Select(x => (ILiteral)vars[x.k]).ToList();
            model.AddExactlyOne(incident);
        }
        model.Add(LinearExpr.Sum(vars) == n / 2);
        model.Minimize(LinearExpr.Sum(edges.Select((e, k) => vars[k] * e.Cost)));
        var solver = new CpSolver { StringParameters = "max_time_in_seconds:60" };
        var status = solver.Solve(model);
        if (status == CpSolverStatus.Infeasible) return null;
        Assert.Equal(CpSolverStatus.Optimal, status);
        return (long)Math.Round(solver.ObjectiveValue);
    }

    private static void AssertPerfectMatching(int n, int[] partner, List<(int U, int V, long Cost)> edges)
    {
        var legal = edges.Select(e => (Math.Min(e.U, e.V), Math.Max(e.U, e.V))).ToHashSet();
        for (var i = 0; i < n; i++)
        {
            Assert.NotEqual(i, partner[i]);
            Assert.Equal(i, partner[partner[i]]);
            Assert.Contains((Math.Min(i, partner[i]), Math.Max(i, partner[i])), legal);
        }
    }

    private static long MatchingCost(int[] partner, List<(int U, int V, long Cost)> edges)
    {
        var cheapest = new Dictionary<(int, int), long>();
        foreach (var e in edges)
        {
            var key = (Math.Min(e.U, e.V), Math.Max(e.U, e.V));
            cheapest[key] = cheapest.TryGetValue(key, out var c) ? Math.Min(c, e.Cost) : e.Cost;
        }
        return Enumerable.Range(0, partner.Length).Where(i => i < partner[i]).Sum(i => cheapest[(i, partner[i])]);
    }

    private static List<Team> RandomTournament(Random rng, int teamCount, int roundsPlayed)
    {
        var clubs = new[] { "A", "B", "C", null };
        var teams = Enumerable.Range(1, teamCount)
            .Select(i => new Team { Name = $"T{i}", Seed = i, Club = clubs[rng.Next(clubs.Length)] })
            .ToList();
        for (var r = 1; r <= roundsPlayed; r++)
        {
            var order = teams.OrderBy(_ => rng.Next()).ToList();
            if (order.Count % 2 == 1)
            {
                var bye = order.FirstOrDefault(t => !t.HadBye) ?? order[0];
                order.Remove(bye);
                bye.RecordBye(r);
                bye.Wins++;
            }
            for (var k = 0; k + 1 < order.Count; k += 2)
            {
                var (aff, neg) = PairRules.CanMeet(order[k], order[k + 1])
                    ? PairRules.AssignSides(order[k], order[k + 1], (_, _) => rng.Next(2) == 0)
                    : (order[k], order[k + 1]);
                // Keep the state legal even when the random draw is not: only record a
                // side when it stays within the one-round imbalance the model allows.
                if (!aff.CanGoAff || !neg.CanGoNeg) continue;
                aff.RecordAff(r);
                neg.RecordNeg(r);
                aff.RecordOpponent(neg);
                if (rng.Next(4) != 0) neg.RecordOpponent(aff);
                if (rng.Next(2) == 0) { aff.Wins++; neg.Losses++; } else { neg.Wins++; aff.Losses++; }
            }
        }
        return teams;
    }
}
