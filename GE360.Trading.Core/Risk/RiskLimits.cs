namespace GE360.Trading.Risk;

/// <summary>
/// Hard limits loaded at startup. Strategies never receive a mutable reference.
/// </summary>
public sealed record RiskLimits(
    decimal MaxRiskPerTradePercent,
    decimal MaxPositionPercent,
    decimal MaxTotalExposurePercent,
    decimal MaxDailyLossPercent,
    decimal MaxDrawdownPercent,
    int MaxOpenPositions,
    decimal MaxSpreadPercent,
    decimal MaxOrderNotional,
    TimeSpan MaxSignalAge,
    bool RequireStopLoss,
    decimal MinStopDistancePercent,
    decimal MaxStopDistancePercent,
    bool AllowShort)
{
    public static RiskLimits ConservativePaperDefaults => new(
        MaxRiskPerTradePercent: 0.005m,
        MaxPositionPercent: 0.10m,
        MaxTotalExposurePercent: 0.50m,
        MaxDailyLossPercent: 0.02m,
        MaxDrawdownPercent: 0.10m,
        MaxOpenPositions: 5,
        MaxSpreadPercent: 0.005m,
        MaxOrderNotional: 20_000m,
        MaxSignalAge: TimeSpan.FromMinutes(2),
        RequireStopLoss: true,
        MinStopDistancePercent: 0.001m,
        MaxStopDistancePercent: 0.05m,
        AllowShort: false);
}
