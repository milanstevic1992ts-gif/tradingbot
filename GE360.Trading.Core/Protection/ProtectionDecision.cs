namespace GE360.Trading.Protection;

public sealed record ProtectionDecision(
    bool Allowed,
    string Code,
    string Reason)
{
    public static ProtectionDecision Allow()
        => new(true, "ALLOWED", "Protection checks passed.");

    public static ProtectionDecision Block(string code, string reason)
        => new(false, code, reason);
}
