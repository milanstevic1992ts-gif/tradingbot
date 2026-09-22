namespace GE360.Trading.Execution;

public sealed record TradeApprovalResult(
    bool Approved,
    string Code,
    string Reason,
    ApprovedOrderIntent? Order)
{
    public static TradeApprovalResult Reject(string code, string reason)
        => new(false, code, reason, null);

    public static TradeApprovalResult Approve(ApprovedOrderIntent order)
        => new(true, "APPROVED", "Signal passed protection and risk.", order);
}
