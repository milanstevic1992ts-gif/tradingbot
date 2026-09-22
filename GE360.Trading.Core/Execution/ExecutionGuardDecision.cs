namespace GE360.Trading.Execution;

public sealed record ExecutionGuardDecision(
    bool Allowed,
    string Code,
    string Reason)
{
    public static ExecutionGuardDecision Allow()
        => new(true, "ALLOWED", "Execution checks passed.");

    public static ExecutionGuardDecision Block(string code, string reason)
        => new(false, code, reason);
}
