using GE360.Trading.Domain;
using GE360.Trading.Risk;

namespace GE360.Trading.Strategies;

public sealed record StrategyContext(
    DateTime UtcTime,
    IReadOnlyDictionary<string, MarketSnapshot> Markets,
    IReadOnlyDictionary<string, StrategyFeatures>? Features = null,
    PortfolioSnapshot? Portfolio = null)
{
    public bool TryGetMarket(string symbol, out MarketSnapshot snapshot)
        => Markets.TryGetValue(symbol, out snapshot!);

    public bool TryGetFeatures(string symbol, out StrategyFeatures features)
    {
        if (Features is not null && Features.TryGetValue(symbol, out var found))
        {
            features = found;
            return true;
        }

        features = null!;
        return false;
    }
}
