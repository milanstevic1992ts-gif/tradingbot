namespace GE360.Trading.Execution;

/// <summary>
/// The only order-shaped object that may cross into an execution adapter.
/// Its constructor is internal so external strategy code cannot forge approval.
/// </summary>
public sealed class ApprovedOrderIntent
{
    internal ApprovedOrderIntent(
        Guid signalId,
        string strategyId,
        string symbol,
        decimal quantity,
        DateTime approvedAtUtc,
        decimal referencePrice,
        decimal? stopPrice,
        decimal? takeProfitPrice,
        string reason,
        bool isRiskReducing)
    {
        SignalId = signalId;
        StrategyId = strategyId;
        Symbol = symbol;
        Quantity = quantity;
        ApprovedAtUtc = approvedAtUtc;
        ReferencePrice = referencePrice;
        StopPrice = stopPrice;
        TakeProfitPrice = takeProfitPrice;
        Reason = reason;
        IsRiskReducing = isRiskReducing;
    }

    public Guid SignalId { get; }
    public string StrategyId { get; }
    public string Symbol { get; }
    public decimal Quantity { get; }
    public DateTime ApprovedAtUtc { get; }
    public decimal ReferencePrice { get; }
    public decimal? StopPrice { get; }
    public decimal? TakeProfitPrice { get; }
    public string Reason { get; }
    public bool IsRiskReducing { get; }
}
