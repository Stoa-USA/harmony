namespace Harmony.Lib.Algorithms;

using Harmony.Lib.Models;

// Which pairs of teams may meet, and which side each takes. Matching works on
// undirected pairs, so every rule here is symmetric in its two arguments.
internal static class PairRules
{
    /// <summary>
    /// A pair is legal when the teams have not met before and at least one side
    /// assignment is legal. History is checked from both teams: a request may list
    /// the meeting on one team only, and either record makes it a rematch.
    /// </summary>
    public static bool IsLegal(Team a, Team b) =>
        !a.HasHit(b) && !b.HasHit(a) && CanMeet(a, b);

    // CanGoAff/CanGoNeg are a legality filter, not a partition: a team with equal aff
    // and neg rounds may take either side, which is every team in an odd-numbered round.
    public static bool CanMeet(Team a, Team b) =>
        (a.CanGoAff && b.CanGoNeg) || (b.CanGoAff && a.CanGoNeg);

    /// <summary>
    /// Assigns sides for a pair that CanMeet. When only one direction is legal it is
    /// used; when both are, <paramref name="preferAff"/> decides.
    /// </summary>
    public static (Team Aff, Team Neg) AssignSides(Team a, Team b, Func<Team, Team, bool> preferAff)
    {
        var aCanAff = a.CanGoAff && b.CanGoNeg;
        var bCanAff = b.CanGoAff && a.CanGoNeg;
        var aTakesAff = aCanAff && (!bCanAff || preferAff(a, b));
        return aTakesAff ? (a, b) : (b, a);
    }

    // Deterministic tie-break for powermatching: the better-seeded team takes aff.
    public static bool LowerSeedNumber(Team a, Team b) => a.Seed <= b.Seed;
}
