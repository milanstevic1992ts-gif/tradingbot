namespace GE360.Trading.Domain;

public sealed record MarketSnapshot(
    string Symbol,
    DateTime UtcTime,
    decimal LastPrice,
    decimal BidPrice,
    decimal AskPrice,
    decimal Volume,
    decimal? Atr = null)
{
    public decimal Spread => AskPrice > 0m && BidPrice > 0m
        ? AskPrice - BidPrice
        : 0m;

    public decimal SpreadPercent => LastPrice > 0m
        ? Spread / LastPrice
        : 0m;
}
