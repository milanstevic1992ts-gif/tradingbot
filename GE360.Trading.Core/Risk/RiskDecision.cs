namespace GE360.Trading.Risk;

public sealed record RiskDecision(
    bool Approved,
    string Code,
    string Reason,
    decimal ApprovedQuantity = 0m)
{
    public static RiskDecision Reject(string code, string reason)
        => new(false, code, reason);

    public static RiskDecision Approve(decimal quantity)
        => new(true, "APPROVED", "Risk checks passed.", quantity);
}
