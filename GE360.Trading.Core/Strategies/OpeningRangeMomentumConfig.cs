namespace GE360.Trading.Strategies;

public sealed record OpeningRangeMomentumConfig(
    decimal MinimumPrice,
    decimal MaximumPrice,
    decimal MinimumRelativeVolume,
    decimal BreakoutBufferPercent,
    decimal MaximumSignalSpreadPercent,
    decimal StopAtrMultiple,
    decimal MinimumStopDistancePercent,
    decimal TakeProfitRiskReward,
    TimeSpan MinimumSignalInterval)
{
    public static OpeningRangeMomentumConfig PaperDefaults => new(
        MinimumPrice: 5m,
        MaximumPrice: 1_000m,
        MinimumRelativeVolume: 1.5m,
        BreakoutBufferPercent: 0.001m,
        MaximumSignalSpreadPercent: 0.003m,
        StopAtrMultiple: 1.0m,
        MinimumStopDistancePercent: 0.0015m,
        TakeProfitRiskReward: 2.0m,
        MinimumSignalInterval: TimeSpan.FromMinutes(5));
}
