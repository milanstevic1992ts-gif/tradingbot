namespace GE360.Trading.Research;

public sealed record ResearchTrade(
    string StrategyId,
    string Symbol,
    DateTime EntryTimeUtc,
    DateTime ExitTimeUtc,
    decimal Quantity,
    decimal ReferenceEntryPrice,
    decimal ReferenceExitPrice,
    decimal FillEntryPrice,
    decimal FillExitPrice,
    decimal Fees,
    string ExitReason)
{
    public decimal GrossPnlBeforeCosts =>
        (ReferenceExitPrice - ReferenceEntryPrice) * Quantity;

    public decimal NetPnl =>
        (FillExitPrice - FillEntryPrice) * Quantity - Fees;

    public decimal TotalCosts => GrossPnlBeforeCosts - NetPnl;
}
