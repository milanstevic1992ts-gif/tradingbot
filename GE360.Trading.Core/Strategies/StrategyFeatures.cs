namespace GE360.Trading.Strategies;

/// <summary>
/// Pre-computed features supplied by the market-data adapter.
/// Strategies consume features; they do not reach into LEAN or a broker.
/// </summary>
public sealed record StrategyFeatures(
    string Symbol,
    decimal Vwap,
    decimal OpeningRangeHigh,
    decimal OpeningRangeLow,
    decimal RelativeVolume,
    bool OpeningRangeComplete);
