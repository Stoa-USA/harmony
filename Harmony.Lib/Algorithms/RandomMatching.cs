using Google.OrTools.Sat;
using Harmony.Lib;
using Harmony.Lib.Models;

namespace Harmony.Lib.Algorithms;

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
        public required BoolVar IsSelected { get; set; }
        public int Cost { get; set; }

        public override string ToString() => $"Team {Aff.Name} vs Team {Neg?.Name ?? "Bye"}";
    }

    public List<Edge> SolveMatching(List<Team> teams)
    {
        var teamCount = teams.Count;
        var byeRoundExists = teamCount % 2 != 0;

        var model = new CpModel();

        var edges = new List<Edge>();
        teams.ForEach(affTeam =>
        {
            if (byeRoundExists && !affTeam.HadBye)
            {
                var byeEdge = new Edge
                {
                    Aff = affTeam,
                    Neg = null,
                    IsSelected = model.NewBoolVar($"bye_{affTeam.Name}"),
                    Cost = _random.Next(1000) // Random cost for bye
                };
                edges.Add(byeEdge);
            }
            if (affTeam.CanGoAff)
            {
                teams.ForEach(negTeam =>
                {
                    if (affTeam != negTeam && negTeam.CanGoNeg && !affTeam.HasHit(negTeam))
                    {
                        var edge = new Edge
                        {
                            Aff = affTeam,
                            Neg = negTeam,
                            IsSelected = model.NewBoolVar($"match_{affTeam.Name}_{negTeam.Name}"),
                            Cost = _random.Next(1000) // Random cost for all legal matchups
                        };
                        edges.Add(edge);
                    }
                });
            }
        });

        // Each team must be in exactly one edge
        teams.ForEach(team =>
        {
            var teamEdges = edges.Where(e => e.Aff == team || e.Neg == team).Select(e => e.IsSelected).ToList();
            model.Add(LinearExpr.Sum(teamEdges) == 1);
        });

        // Total edges should equal expected matchup count
        var allEdges = edges.Select(e => e.IsSelected).ToList();
        var expectedMatchupCount = teamCount / 2;
        if (byeRoundExists)
        {
            expectedMatchupCount++;
        }
        model.Add(LinearExpr.Sum(allEdges) == expectedMatchupCount);

        // Minimize cost (which is random, so we get random valid matching)
        var costTerms = edges.Select(e => e.IsSelected * e.Cost).ToList();
        model.Minimize(LinearExpr.Sum(costTerms));

        // Solve the model
        var solver = SolverDefaults.CreateSolver();
        var status = solver.Solve(model);

        if (status == CpSolverStatus.Optimal || status == CpSolverStatus.Feasible)
        {
            var answer = edges.Where(e => solver.BooleanValue(e.IsSelected)).ToList();
            return answer;
        }

        throw new CannotPairException();
    }
}
