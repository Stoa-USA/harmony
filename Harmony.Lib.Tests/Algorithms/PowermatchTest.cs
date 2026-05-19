namespace Harmony.Lib.Tests;

using System.Text.Json;
using Harmony.Lib.Models;

public class PowermatchTest
{
    // Regression test for stoaapp issue #1579 (Parli Espresso Cup, 2026-05-11).
    // Payload captured from CloudWatch invocation at 2026-05-12T03:14:58Z.
    // Engine handed the bye to seed 2 (63160, 5-0); the bye should always go to the
    // worst-ranked team that is still bye-eligible (no prior bye / forfeit-win).
    // In this R6 that is team 62534 (seed 31, 1-4, isByeEligible=true).
    [Fact]
    public void EspressoCupRound6_ByeGoesToWorstEligibleTeam()
    {
        var (teams, roundNumber) = LoadScenario("EspressoR6.json");

        var round = new Round { Number = roundNumber };
        round.PowermatchHighLow(teams);

        var bye = round.Matchups.SingleOrDefault(m => m.IsBye);
        Assert.NotNull(bye);
        Assert.Equal("62534", bye!.Aff.Name);
    }

    // Regression for production pairing at 2026-05-19T00:35:45Z (Lincoln Douglas, 202 teams,
    // round 3, no byes needed, all teams 1-1 on Aff/Neg). Win-bracket sizes are 53/96/53, so
    // the structural minimum number of cross-bracket matchups is 2 (one 2W pulled down to 1W
    // and one 0W pulled up to 1W). Production returned 10 cross-bracket matchups.
    [Fact]
    public void LincolnDouglas202R3_MinimizesCrossBracketPullups()
    {
        var (teams, roundNumber) = LoadScenario("LincolnDouglas202R3.json");

        var round = new Round { Number = roundNumber };
        round.PowermatchHighLow(teams);

        var winsByName = teams.ToDictionary(t => t.Name, t => t.Wins);
        var crossBracket = round.Matchups
            .Where(m => !m.IsBye && winsByName[m.Aff.Name] != winsByName[m.Neg!.Name])
            .ToList();

        Assert.Equal(2, crossBracket.Count);
    }

    private static (List<Team> teams, int roundNumber) LoadScenario(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
        using var stream = File.OpenRead(path);
        var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var roundNumber = root.GetProperty("roundNumber").GetInt32();

        var dtos = root.GetProperty("teams").EnumerateArray()
            .Select(t => new
            {
                Name = t.GetProperty("name").GetString()!,
                IsByeEligible = t.GetProperty("isByeEligible").GetBoolean(),
                Wins = t.GetProperty("wins").GetInt32(),
                Losses = t.GetProperty("losses").GetInt32(),
                AffRounds = t.GetProperty("affRounds").GetInt32(),
                NegRounds = t.GetProperty("negRounds").GetInt32(),
                Seed = t.GetProperty("seed").GetInt32(),
                Club = t.TryGetProperty("club", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null,
                OpponentHistory = t.GetProperty("opponentHistory").EnumerateArray().Select(o => o.GetString()!).ToList(),
            })
            .ToList();

        var teams = dtos.Select(d => new Team
        {
            Name = d.Name,
            Wins = d.Wins,
            Losses = d.Losses,
            AffRounds = d.AffRounds,
            NegRounds = d.NegRounds,
            Seed = d.Seed,
            Club = d.Club,
        }).ToList();

        var byName = teams.ToDictionary(t => t.Name);
        foreach (var d in dtos)
        {
            var team = byName[d.Name];
            foreach (var opp in d.OpponentHistory)
            {
                if (byName.TryGetValue(opp, out var oppTeam))
                    team.RecordOpponent(oppTeam);
            }
            // PairingService marks bye-ineligible teams as having had a bye so the algorithm skips them.
            if (!d.IsByeEligible)
                team.RecordBye(-1);
        }

        return (teams, roundNumber);
    }

    [Fact]
    public void SimplePowermatchTest()
    {
        var teamA = new Team { Name = "teamA", Wins = 1, Losses = 0, AffRounds = 1, NegRounds = 0 };
        var teamB = new Team { Name = "teamB", Wins = 0, Losses = 1, AffRounds = 1, NegRounds = 0 };
        var teamC = new Team { Name = "teamC", Wins = 0, Losses = 1, AffRounds = 0, NegRounds = 1 };
        var teamD = new Team { Name = "teamD", Wins = 1, Losses = 0, AffRounds = 0, NegRounds = 1 };
        var round = new Round { Number = 2 };
        round.PowermatchHighLow([teamA, teamB, teamC, teamD]);
        Assert.Equal(2, round.Matchups.Count);
        Assert.Single(round.Matchups, m => m.Aff == teamD && m.Neg == teamA);
        Assert.Single(round.Matchups, m => m.Aff == teamC && m.Neg == teamB);
    }

    [Fact]
    public void PowermatchPrefersToSmallPullupsOverABigPullupTest()
    {
        var teamA = new Team { Name = "teamA", Wins = 2, Losses = 0, AffRounds = 1, NegRounds = 0 };
        var teamB = new Team { Name = "teamB", Wins = 1, Losses = 1, AffRounds = 1, NegRounds = 0 };
        var teamC = new Team { Name = "teamC", Wins = 1, Losses = 1, AffRounds = 0, NegRounds = 1 };
        var teamD = new Team { Name = "teamD", Wins = 0, Losses = 2, AffRounds = 0, NegRounds = 1 };
        var round = new Round { Number = 3 };
        round.PowermatchHighLow([teamA, teamB, teamC, teamD]);
        Assert.Equal(2, round.Matchups.Count);
        Assert.Single(round.Matchups, m => m.Aff == teamD && m.Neg == teamB);
        Assert.Single(round.Matchups, m => m.Aff == teamC && m.Neg == teamA);
    }

    [Fact]
    public void PowermatchGivesABye()
    {
        var teamA = new Team { Name = "teamA", Wins = 0, Losses = 0, AffRounds = 0, NegRounds = 0 };
        var teamB = new Team { Name = "teamB", Wins = 0, Losses = 0, AffRounds = 0, NegRounds = 0 };
        var teamC = new Team { Name = "teamC", Wins = 0, Losses = 0, AffRounds = 0, NegRounds = 0 };
        var round = new Round { Number = 1 };
        round.PowermatchHighLow([teamA, teamB, teamC]);
        Assert.Equal(2, round.Matchups.Count);
        Assert.Single(round.Matchups, m => m.IsBye);
    }

    [Fact]
    public void WorstTeamGetsBye()
    {
        var teamA = new Team { Name = "teamA", Wins = 10, Losses = 0, AffRounds = 0, NegRounds = 0 };
        var teamB = new Team { Name = "teamB", Wins = 10, Losses = 0, AffRounds = 0, NegRounds = 0 };
        var teamC = new Team { Name = "teamC", Wins = 0, Losses = 0, AffRounds = 0, NegRounds = 0 };
        var round = new Round { Number = 1 };
        round.PowermatchHighLow([teamA, teamB, teamC]);
        Assert.Equal(2, round.Matchups.Count);
        Assert.Single(round.Matchups, m => m.IsBye && m.Aff == teamC);
    }

    [Fact]
    public void WorstTeamWithoutExistingByeGetsBye()
    {
        var teamA = new Team { Name = "teamA", Wins = 10, Losses = 0, AffRounds = 0, NegRounds = 0 };
        var teamB = new Team { Name = "teamB", Wins = 9, Losses = 0, AffRounds = 0, NegRounds = 0 };
        var teamC = new Team { Name = "teamC", Wins = 0, Losses = 0, AffRounds = 0, NegRounds = 0 };
        teamC.RecordBye(1);
        var round = new Round { Number = 1 };
        round.PowermatchHighLow([teamA, teamB, teamC]);
        Assert.Equal(2, round.Matchups.Count);
        Assert.Single(round.Matchups, m => m.IsBye && m.Aff == teamB);
    }

    [Fact]
    public void TeamsCannotHitPrior()
    {
        var teamA = new Team { Name = "teamA", Wins = 2, Losses = 0, AffRounds = 1, NegRounds = 0 };
        var teamB = new Team { Name = "teamB", Wins = 1, Losses = 1, AffRounds = 1, NegRounds = 0 };
        var teamC = new Team { Name = "teamC", Wins = 1, Losses = 1, AffRounds = 0, NegRounds = 1 };
        var teamD = new Team { Name = "teamD", Wins = 0, Losses = 2, AffRounds = 0, NegRounds = 1 };
        teamA.RecordOpponent(teamC);
        teamC.RecordOpponent(teamA);
        teamB.RecordOpponent(teamD);
        teamD.RecordOpponent(teamB);
        var round = new Round { Number = 3 };
        round.PowermatchHighLow([teamA, teamB, teamC, teamD]);
        Assert.Equal(2, round.Matchups.Count);
        Assert.Single(round.Matchups, m => m.Aff == teamC && m.Neg == teamB);
        Assert.Single(round.Matchups, m => m.Aff == teamD && m.Neg == teamA);
    }

    [Fact]
    public void HighLowTest()
    {
        var teamA = new Team { Name = "teamA", Wins = 0, Losses = 0, AffRounds = 0, NegRounds = 0, Seed = 1 };
        var teamB = new Team { Name = "teamB", Wins = 0, Losses = 0, AffRounds = 0, NegRounds = 0, Seed = 2 };
        var teamC = new Team { Name = "teamC", Wins = 0, Losses = 0, AffRounds = 0, NegRounds = 0, Seed = 3 };
        var teamD = new Team { Name = "teamD", Wins = 0, Losses = 0, AffRounds = 0, NegRounds = 0, Seed = 4 };
        var teamE = new Team { Name = "teamE", Wins = 0, Losses = 0, AffRounds = 0, NegRounds = 0, Seed = 5 };
        var teamF = new Team { Name = "teamF", Wins = 0, Losses = 0, AffRounds = 0, NegRounds = 0, Seed = 6 };
        var round = new Round { Number = 1 };
        round.PowermatchHighLow([teamA, teamB, teamC, teamD, teamE, teamF]);
        Assert.Equal(3, round.Matchups.Count);
        Assert.Single(round.Matchups, m => m.Contains(teamA) && m.Contains(teamF));
        Assert.Single(round.Matchups, m => m.Contains(teamB) && m.Contains(teamE));
        Assert.Single(round.Matchups, m => m.Contains(teamC) && m.Contains(teamD));
    }

    [Fact]
    public void OpponentStrengthAveragesOpponentWins()
    {
        var opp1 = new Team { Name = "opp1", Wins = 4 };
        var opp2 = new Team { Name = "opp2", Wins = 2 };
        var opp3 = new Team { Name = "opp3", Wins = 0 };
        var team = new Team { Name = "team" };
        team.RecordOpponent(opp1);
        team.RecordOpponent(opp2);
        team.RecordOpponent(opp3);
        Assert.Equal(2.0, team.OpponentStrength);
    }

    [Fact]
    public void OpponentStrengthIsZeroWithNoOpponents()
    {
        var team = new Team { Name = "team" };
        Assert.Equal(0.0, team.OpponentStrength);
    }

    [Fact]
    public void MatchupCostFavorsWeakestSchedulePullup()
    {
        // P (4-0) must pull up exactly one of Q/R/S (all 3-1).
        // Q faced strong opponents (avg 3.0), R medium (avg 2.0), S weak (avg 1.0).
        // Pull-up rule: prefer the lower team with weakest schedule, so P-S < P-R < P-Q.
        var p = new Team { Name = "P", Wins = 4, Losses = 0, Seed = 1 };
        var q = TeamWithOpponentWins("Q", wins: 3, losses: 1, seed: 2, opponentWins: [3, 3, 3, 3]);
        var r = TeamWithOpponentWins("R", wins: 3, losses: 1, seed: 3, opponentWins: [2, 2, 2, 2]);
        var s = TeamWithOpponentWins("S", wins: 3, losses: 1, seed: 4, opponentWins: [1, 1, 1, 1]);

        Assert.Equal(3.0, q.OpponentStrength);
        Assert.Equal(2.0, r.OpponentStrength);
        Assert.Equal(1.0, s.OpponentStrength);

        Assert.True(p.MatchupCost(s) < p.MatchupCost(r));
        Assert.True(p.MatchupCost(r) < p.MatchupCost(q));
    }

    [Fact]
    public void MatchupCostFavorsWeakestScheduleOnDoublePullup()
    {
        // Forced double pull-up (winDiff=2): heuristic still applies.
        var p = new Team { Name = "P", Wins = 4, Losses = 0, Seed = 1 };
        var q = TeamWithOpponentWins("Q", wins: 2, losses: 2, seed: 2, opponentWins: [3, 3, 3, 3]);
        var r = TeamWithOpponentWins("R", wins: 2, losses: 2, seed: 3, opponentWins: [1, 1, 1, 1]);
        Assert.True(p.MatchupCost(r) < p.MatchupCost(q));
    }

    [Fact]
    public void MatchupCostPullupTermDoesNotApplyWithinSameBracket()
    {
        // Within winDiff=0, OpponentStrength must not bias the cost.
        var a = TeamWithOpponentWins("A", wins: 3, losses: 1, seed: 1, opponentWins: [3, 3, 3, 3]);
        var b = TeamWithOpponentWins("B", wins: 3, losses: 1, seed: 3, opponentWins: [3, 3, 3, 3]);
        var c = TeamWithOpponentWins("C", wins: 3, losses: 1, seed: 2, opponentWins: [1, 1, 1, 1]);
        var d = TeamWithOpponentWins("D", wins: 3, losses: 1, seed: 4, opponentWins: [1, 1, 1, 1]);
        // Same seed spread (2) for both pairs; same winDiff (0). Cost must be equal,
        // i.e. opponent strength must NOT influence cost when teams are in the same bracket.
        Assert.Equal(a.MatchupCost(b), c.MatchupCost(d));
    }

    [Fact]
    public void PowermatchPullsUpWeakestScheduleTeam()
    {
        // Same construction as MatchupCostFavorsWeakestSchedulePullup but as a
        // full round pairing. Seeds are intentionally inverted (Q worst, S best)
        // so that the old seed-spread proxy would have pulled up Q; the new
        // opponent-strength rule should pull up S.
        var p = new Team { Name = "P", Wins = 4, Losses = 0, AffRounds = 2, NegRounds = 2, Seed = 1 };
        var q = TeamWithOpponentWins("Q", wins: 3, losses: 1, seed: 4, opponentWins: [3, 3, 3, 3], affRounds: 2, negRounds: 2);
        var r = TeamWithOpponentWins("R", wins: 3, losses: 1, seed: 3, opponentWins: [2, 2, 2, 2], affRounds: 2, negRounds: 2);
        var s = TeamWithOpponentWins("S", wins: 3, losses: 1, seed: 2, opponentWins: [1, 1, 1, 1], affRounds: 2, negRounds: 2);

        Assert.Equal(3.0, q.OpponentStrength);
        Assert.Equal(2.0, r.OpponentStrength);
        Assert.Equal(1.0, s.OpponentStrength);
        Assert.Equal(4, q.Opponents.Count);
        Assert.Equal(4, r.Opponents.Count);
        Assert.Equal(4, s.Opponents.Count);

        var round = new Round { Number = 5 };
        round.PowermatchHighLow([p, q, r, s]);

        Assert.Equal(2, round.Matchups.Count);
        Assert.Single(round.Matchups, m => m.Contains(p) && m.Contains(s));
        Assert.Single(round.Matchups, m => m.Contains(q) && m.Contains(r));
    }

    [Fact]
    public void PullupTiebreakerFallsBackToSeedSpread()
    {
        // Q and R are tied on opponent strength (both 2.0); S is stronger (4.0)
        // and should not be the pull-up. Among the tied pair, the worse-seeded
        // candidate (Q at seed 4) gets pulled up because of the larger spread to P.
        var p = new Team { Name = "P", Wins = 4, Losses = 0, AffRounds = 2, NegRounds = 2, Seed = 1 };
        var q = TeamWithOpponentWins("Q", wins: 3, losses: 1, seed: 4, opponentWins: [2, 2, 2, 2], affRounds: 2, negRounds: 2);
        var r = TeamWithOpponentWins("R", wins: 3, losses: 1, seed: 3, opponentWins: [2, 2, 2, 2], affRounds: 2, negRounds: 2);
        var s = TeamWithOpponentWins("S", wins: 3, losses: 1, seed: 2, opponentWins: [4, 4, 4, 4], affRounds: 2, negRounds: 2);

        var round = new Round { Number = 5 };
        round.PowermatchHighLow([p, q, r, s]);

        Assert.Equal(2, round.Matchups.Count);
        Assert.Single(round.Matchups, m => m.Contains(p) && m.Contains(q));
        Assert.Single(round.Matchups, m => m.Contains(r) && m.Contains(s));
    }

    private static Team TeamWithOpponentWins(
        string name, int wins, int losses, int seed, int[] opponentWins,
        int affRounds = 0, int negRounds = 0)
    {
        var team = new Team { Name = name, Wins = wins, Losses = losses, Seed = seed, AffRounds = affRounds, NegRounds = negRounds };
        for (var i = 0; i < opponentWins.Length; i++)
        {
            team.RecordOpponent(new Team { Name = $"{name}_opp{i}", Wins = opponentWins[i] });
        }
        return team;
    }

    [Fact]
    public void ShouldNotPairIfByeIsNeededButNoOneEligible()
    {
        var teamA = new Team { Name = "teamA" };
        var teamB = new Team { Name = "teamB" };
        var teamC = new Team { Name = "teamC" };
        teamA.RecordBye(1);
        teamB.RecordBye(1);
        teamC.RecordBye(1);
        Assert.Throws<CannotPairException>(() => new Round { Number = 1 }.PowermatchHighLow([teamA, teamB, teamC]));
    }

    [Fact(Skip = "Long-running benchmark; enable to profile pairing performance.")]
    public void LargeScaleTournament_110Teams_6Rounds()
    {
        var teams = new List<Team>();

        // Create 110 teams - all starting fresh
        for (int i = 0; i < 110; i++)
        {
            teams.Add(new Team
            {
                Name = $"Team{i:D3}",
                Wins = 0,
                Losses = 0,
                AffRounds = 0,
                NegRounds = 0,
                Seed = i + 1,
                Club = i % 10 == 0 ? $"Club{i / 10}" : null // 10% same club
            });
        }

        var roundTimes = new List<long>();
        var totalSw = System.Diagnostics.Stopwatch.StartNew();

        // Simulate 6 full rounds using the PowermatchHighLow algorithm
        for (int roundNum = 1; roundNum <= 6; roundNum++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var round = new Round { Number = roundNum };
            round.PowermatchHighLow(teams);
            sw.Stop();
            roundTimes.Add(sw.ElapsedMilliseconds);

            // Verify matchups are valid
            Assert.Equal(55, round.Matchups.Count); // 110 teams = 55 matchups
            Assert.All(round.Matchups, m => Assert.False(m.IsBye)); // Even number, no byes
            Assert.All(round.Matchups, m =>
            {
                Assert.False(m.Aff.HasHit(m.Neg!));
                Assert.False(m.Neg!.HasHit(m.Aff));
            });

            // Record the matchups and randomly assign wins
            var random = new Random(42 + roundNum);
            foreach (var matchup in round.Matchups)
            {
                // Record opponents
                matchup.Aff.RecordOpponent(matchup.Neg!);
                matchup.Neg!.RecordOpponent(matchup.Aff);

                // Record sides
                matchup.Aff.RecordAff(roundNum);
                matchup.Neg.RecordNeg(roundNum);

                // Randomly assign win
                if (random.Next(2) == 0)
                {
                    matchup.Aff.Wins++;
                    matchup.Neg.Losses++;
                }
                else
                {
                    matchup.Neg.Wins++;
                    matchup.Aff.Losses++;
                }
            }
        }

        totalSw.Stop();

        // Performance check - each round should complete in reasonable time
        for (int i = 0; i < roundTimes.Count; i++)
        {
            Assert.True(roundTimes[i] < 30000,
                $"Round {i + 1} took {roundTimes[i]}ms, expected < 30s");
        }

        Console.WriteLine($"110 teams, 6 rounds completed:");
        for (int i = 0; i < roundTimes.Count; i++)
        {
            Console.WriteLine($"  Round {i + 1}: {roundTimes[i]}ms");
        }
        Console.WriteLine($"Total time: {totalSw.ElapsedMilliseconds}ms");
        Console.WriteLine($"Average per round: {roundTimes.Average():F0}ms");
    }
}