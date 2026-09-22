using GE360.Trading.Domain;
using GE360.Trading.Strategies;

namespace GE360.Trading.Features;

public sealed record IntradayFeatureOutput(
    MarketSnapshot Market,
    StrategyFeatures Features);
