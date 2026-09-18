using Harmony.Lib;
using Harmony.Lib.Models;

namespace Harmony.Lib.Algorithms;

// Produces a legal pairing chosen at random: every legal pair (and, in an odd round,
// every bye-eligible team's bye) gets a random cost and the exact min-cost matching
// is taken. Any legal pairing is what the caller wants; the random costs just make
// the choice unstructured, and the exact solver makes it instant.
public class RandomMatching
{
    private readonly Random _random;

    public RandomMatching(Random? random = null)
    {
        _random = random ?? new Random();
    }

    public class Edge
    {
        public required Team Aff { get; init; }
        public Team? Neg { get; init; }

        public override string ToString() => $"Team {Aff.Name} vs Team {Neg?.Name ?? "Bye"}";
    }

    public List<Edge> SolveMatching(List<Team> teams)
    {
        var byeRoundExists = teams.Count % 2 != 0;

        // In an odd round the bye is an extra vertex that only bye-eligible teams can
        // be matched to, so the bye is chosen as part of the matching, not up front.
        var byeVertex = byeRoundExists ? teams.Count : -1;
        var vertexCount = byeRoundExists ? teams.Count + 1 : teams.Count;

        var candidates = new List<(int U, int V, long Cost)>();
        for (var i = 0; i < teams.Count; i++)
        {
            if (byeRoundExists && !teams[i].HadBye)
                candidates.Add((i, byeVertex, _random.Next(1000)));

            for (var j = i + 1; j < teams.Count; j++)
            {
                if (!PairRules.IsLegal(teams[i], teams[j])) continue;
                candidates.Add((i, j, _random.Next(1000)));
            }
        }

        var partner = MinCostPerfectMatching.Solve(vertexCount, candidates)
            ?? throw new CannotPairException();

        var edges = new List<Edge>();
        for (var i = 0; i < teams.Count; i++)
        {
            var j = partner[i];
            if (j == byeVertex)
            {
                edges.Add(new Edge { Aff = teams[i], Neg = null });
                continue;
            }
            if (j < i) continue;
            var (aff, neg) = PairRules.AssignSides(teams[i], teams[j], preferAff: (_, _) => _random.Next(2) == 0);
            edges.Add(new Edge { Aff = aff, Neg = neg });
        }

        return edges;
    }
}
