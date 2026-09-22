namespace GE360.Trading.Risk;

public sealed record RiskDecision(
    bool Approved,
    string Code,
    string Reason,
    decimal ApprovedQuantity = 0m,
    bool TriggerHalt = false)
{
    public static RiskDecision Reject(string code, string reason, bool triggerHalt = false)
        => new(false, code, reason, 0m, triggerHalt);

    public static RiskDecision Approve(decimal quantity)
        => new(true, "APPROVED", "Risk checks passed.", quantity, false);
}
