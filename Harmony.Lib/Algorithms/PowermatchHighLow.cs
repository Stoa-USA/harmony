using Google.OrTools.Sat;
using Harmony.Lib;
using Harmony.Lib.Models;
public class PowermatchHighLow
{
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

            edges.Add(new Edge
            {
                Aff = byeTeam,
                Neg = null,
                IsSelected = model.NewBoolVar($"bye_{byeTeam.Name}"),
                Cost = 0
            });
        }

        teams.ForEach(affTeam =>
        {
            if (affTeam == byeTeam) return;
            if (affTeam.CanGoAff)
            {
                teams.ForEach(negTeam =>
                {
                    if (negTeam == byeTeam) return;
                    if (affTeam != negTeam && negTeam.CanGoNeg && !affTeam.HasHit(negTeam))
                    {
                        var edge = new Edge
                        {
                            Aff = affTeam,
                            Neg = negTeam,
                            IsSelected = model.NewBoolVar($"match_{affTeam.Name}_{negTeam.Name}"),
                            Cost = affTeam.MatchupCost(negTeam)
                        };
                        edges.Add(edge);
                    }
                });
            }
        });
        
        // Removed verbose edge logging for performance

        teams.ForEach(team =>
        {
            var teamEdges = edges.Where(e => e.Aff == team || e.Neg == team).Select(e => e.IsSelected).ToList();
            model.Add(LinearExpr.Sum(teamEdges) == 1);
        });

        var allEdges = edges.Select(e => e.IsSelected).ToList();
        var expectedMatchupCount = teamCount / 2;
        if (byeRoundExists)
        {
            expectedMatchupCount++;
        }
        model.Add(LinearExpr.Sum(allEdges) == expectedMatchupCount);

        var costTerms = edges.Select(e => e.IsSelected * e.Cost).ToList();
        model.Minimize(LinearExpr.Sum(costTerms));

        // Solve the model
        var solver = new CpSolver();
        solver.StringParameters = "max_time_in_seconds:19";
        var status = solver.Solve(model);

        if (status == CpSolverStatus.Optimal || status == CpSolverStatus.Feasible)
        {
            // Return only the selected edges
            var answer = edges.Where(e => solver.BooleanValue(e.IsSelected)).ToList();
            return answer;
        }

        throw new CannotPairException();
    }
}
