namespace GE360.Trading.LeanAdapter;

public sealed record LeanExecutionResult(
    bool Submitted,
    string Code,
    string Reason,
    int? LeanOrderId = null)
{
    public static LeanExecutionResult Reject(string code, string reason)
        => new(false, code, reason);

    public static LeanExecutionResult Success(int orderId)
        => new(true, "SUBMITTED", "LEAN accepted the order request.", orderId);
}
