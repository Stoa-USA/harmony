using Harmony.Lib;
using Harmony.Lib.Algorithms;
using Harmony.Lib.Models;

public class PowermatchHighLow
{
    public class Edge
    {
        public required Team Aff { get; init; }
        public Team? Neg { get; init; }
        public int Cost { get; init; }

        public override string ToString() => $"Team {Aff.Name} vs Team {Neg?.Name ?? "Bye"}";
    }

    public List<Edge> SolveMatching(List<Team> teams)
    {
        var byeRoundExists = teams.Count % 2 != 0;
        var edges = new List<Edge>();

        // The bye is a hard pre-assignment: worst bye-eligible team (fewest wins,
        // tie-broken by lowest seed = highest seed number) always gets it. Letting
        // the solver weigh the bye against matchup costs allowed it to hand the bye
        // to a top seed when avoiding a bracket pull-up was cheaper (issue #1579).
        Team? byeTeam = null;
        if (byeRoundExists)
        {
            byeTeam = teams
                .Where(t => !t.HadBye)
                .OrderBy(t => t.Wins)
                .ThenByDescending(t => t.Seed)
                .FirstOrDefault();
            if (byeTeam == null) throw new CannotPairException();

            edges.Add(new Edge { Aff = byeTeam, Neg = null, Cost = 0 });
        }

        var pool = teams.Where(t => t != byeTeam).ToList();

        // Matchup cost is symmetric, so each legal pair is one undirected edge; which
        // side each team takes is decided after the matching (see PairRules.AssignSides).
        var candidates = new List<(int U, int V, long Cost)>();
        for (var i = 0; i < pool.Count; i++)
        {
            for (var j = i + 1; j < pool.Count; j++)
            {
                var a = pool[i];
                var b = pool[j];
                if (!PairRules.IsLegal(a, b)) continue;
                candidates.Add((i, j, a.MatchupCost(b)));
            }
        }

        var partner = MinCostPerfectMatching.Solve(pool.Count, candidates)
            ?? throw new CannotPairException();

        for (var i = 0; i < pool.Count; i++)
        {
            var j = partner[i];
            if (j < i) continue;
            var (aff, neg) = PairRules.AssignSides(pool[i], pool[j], preferAff: PairRules.LowerSeedNumber);
            edges.Add(new Edge { Aff = aff, Neg = neg, Cost = aff.MatchupCost(neg) });
        }

        return edges;
    }
}
