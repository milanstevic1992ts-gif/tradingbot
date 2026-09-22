namespace GE360.Trading.Protection;

public sealed record TradeOutcome(
    string StrategyId,
    string Symbol,
    DateTime ClosedAtUtc,
    decimal RealizedPnl);
