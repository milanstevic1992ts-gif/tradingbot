using GE360.Trading.Domain;

namespace GE360.Trading.Research;

public sealed record ResearchMarketAssumptions(
    decimal SyntheticSpreadPercent)
{
    public static ResearchMarketAssumptions ConservativeSmokeDefaults => new(
        SyntheticSpreadPercent: 0.0002m);

    public MarketSnapshot Apply(MarketSnapshot market)
    {
        ArgumentNullException.ThrowIfNull(market);

        if (SyntheticSpreadPercent < 0m || SyntheticSpreadPercent >= 1m)
        {
            throw new InvalidOperationException("Synthetic spread must be in [0,1).");
        }

        var halfSpread = market.LastPrice * SyntheticSpreadPercent / 2m;
        return market with
        {
            BidPrice = Math.Max(0m, market.LastPrice - halfSpread),
            AskPrice = market.LastPrice + halfSpread
        };
    }

    public MarketSnapshot AtPrice(
        string symbol,
        DateTime utcTime,
        decimal price,
        decimal volume,
        decimal? atr = null)
        => Apply(new MarketSnapshot(
            symbol,
            utcTime,
            price,
            price,
            price,
            volume,
            atr));
}
