namespace Harmony.Lib.Algorithms;

using Google.OrTools.Sat;

// Shared CP-SAT settings. Every solve runs inside a single Lambda invocation, so each
// one needs a wall-clock bound comfortably under the function timeout -- an unbounded
// solve burns the whole invocation and returns nothing at all. Both algorithms build
// their solver here so the bound cannot be forgotten for one path and not the other.
internal static class SolverDefaults
{
    // Mirrors --timeout in scripts/deploy-lambda.sh. Duplicated here so the solver bound
    // can be checked against it in tests; change both together.
    public const int LambdaTimeoutSeconds = 300;

    // A ceiling, not a cost: a typical round finishes in well under a second and returns
    // immediately. Sized for the worst observed real round (202 teams), where the model
    // carries roughly 40,000 boolean edge variables and CP-SAT needs about 150s just to
    // clear presolve and reach a first feasible solution. At the old 19s it returned
    // status UNKNOWN having done no search at all -- no pairing, not even a bad one.
    public const int MaxTimeInSeconds = 240;

    // Room for cold start, building the model, and serializing the response after the
    // solver gives up its budget.
    public const int RequiredHeadroomSeconds = 45;

    public static string StringParameters => $"max_time_in_seconds:{MaxTimeInSeconds}";

    public static CpSolver CreateSolver() => new() { StringParameters = StringParameters };
}
