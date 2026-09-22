using GE360.Trading.Domain;

namespace GE360.Trading.Risk;

public sealed record PortfolioSnapshot(
    DateTime UtcTime,
    TradingState TradingState,
    decimal Equity,
    decimal PeakEquity,
    decimal DayStartEquity,
    decimal Cash,
    IReadOnlyDictionary<string, PositionSnapshot> Positions)
{
    public decimal GrossExposure => Positions.Values.Sum(position => position.Notional);

    public decimal DrawdownPercent => PeakEquity > 0m
        ? Math.Max(0m, (PeakEquity - Equity) / PeakEquity)
        : 0m;

    public decimal DailyLossPercent => DayStartEquity > 0m && Equity < DayStartEquity
        ? (DayStartEquity - Equity) / DayStartEquity
        : 0m;

    public int OpenPositionCount => Positions.Values.Count(position => position.Quantity != 0m);
}
