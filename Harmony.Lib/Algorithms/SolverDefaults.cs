namespace Harmony.Lib.Algorithms;

using Google.OrTools.Sat;

// Shared CP-SAT settings. Every solve runs inside a single Lambda invocation with a
// 40s function timeout, so each one needs a wall-clock bound comfortably under it --
// an unbounded solve burns the whole invocation and returns nothing at all.
// Both algorithms build their solver here so the bound cannot be forgotten again.
internal static class SolverDefaults
{
    public const int MaxTimeInSeconds = 19;

    public static string StringParameters => $"max_time_in_seconds:{MaxTimeInSeconds}";

    public static CpSolver CreateSolver() => new() { StringParameters = StringParameters };
}
