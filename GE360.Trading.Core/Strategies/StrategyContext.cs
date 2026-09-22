using GE360.Trading.Domain;

namespace GE360.Trading.Strategies;

public sealed record StrategyContext(
    DateTime UtcTime,
    IReadOnlyDictionary<string, MarketSnapshot> Markets)
{
    public bool TryGetMarket(string symbol, out MarketSnapshot snapshot)
        => Markets.TryGetValue(symbol, out snapshot!);
}
