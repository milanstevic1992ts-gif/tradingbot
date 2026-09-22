using GE360.Trading.Domain;

namespace GE360.Trading.Strategies;

/// <summary>
/// Transparent long-only intraday signal model:
/// completed opening range + relative volume + VWAP confirmation + breakout.
/// It emits SignalIntent only; sizing and execution are owned elsewhere.
/// </summary>
public sealed class OpeningRangeMomentumStrategy : IGe360Strategy
{
    private readonly OpeningRangeMomentumConfig _config;
    private readonly Dictionary<string, DateTime> _lastSignalBySymbol =
        new(StringComparer.OrdinalIgnoreCase);

    public OpeningRangeMomentumStrategy(
        OpeningRangeMomentumConfig? config = null,
        string strategyId = "opening-range-momentum-v1")
    {
        _config = config ?? OpeningRangeMomentumConfig.PaperDefaults;
        StrategyId = string.IsNullOrWhiteSpace(strategyId)
            ? throw new ArgumentException("Strategy id is required.", nameof(strategyId))
            : strategyId;

        ValidateConfig(_config);
    }

    public string StrategyId { get; }

    public IEnumerable<SignalIntent> Evaluate(StrategyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Features is null)
        {
            yield break;
        }

        foreach (var feature in context.Features.Values)
        {
            if (!context.TryGetMarket(feature.Symbol, out var market))
            {
                continue;
            }

            if (context.Portfolio is not null &&
                context.Portfolio.Positions.TryGetValue(feature.Symbol, out var existingPosition) &&
                existingPosition.Quantity != 0m)
            {
                continue;
            }

            if (!feature.OpeningRangeComplete ||
                market.LastPrice < _config.MinimumPrice ||
                market.LastPrice > _config.MaximumPrice ||
                feature.RelativeVolume < _config.MinimumRelativeVolume ||
                market.SpreadPercent > _config.MaximumSignalSpreadPercent)
            {
                continue;
            }

            if (market.Atr is null || market.Atr <= 0m ||
                feature.Vwap <= 0m ||
                feature.OpeningRangeHigh <= 0m ||
                feature.OpeningRangeLow <= 0m)
            {
                continue;
            }

            if (_lastSignalBySymbol.TryGetValue(feature.Symbol, out var lastSignal) &&
                context.UtcTime - lastSignal < _config.MinimumSignalInterval)
            {
                continue;
            }

            var breakoutLevel = feature.OpeningRangeHigh * (1m + _config.BreakoutBufferPercent);
            if (market.LastPrice <= breakoutLevel || market.LastPrice <= feature.Vwap)
            {
                continue;
            }

            var atrStopDistance = market.Atr.Value * _config.StopAtrMultiple;
            var minimumStopDistance = market.LastPrice * _config.MinimumStopDistancePercent;
            var stopDistance = Math.Max(atrStopDistance, minimumStopDistance);
            if (stopDistance <= 0m || stopDistance >= market.LastPrice)
            {
                continue;
            }

            var stopPrice = market.LastPrice - stopDistance;
            var takeProfitPrice =
                market.LastPrice + stopDistance * _config.TakeProfitRiskReward;

            var confidence = ComputeConfidence(
                feature.RelativeVolume,
                market.LastPrice,
                breakoutLevel);

            _lastSignalBySymbol[feature.Symbol] = context.UtcTime;

            yield return SignalIntent.Create(
                StrategyId,
                feature.Symbol,
                SignalDirection.Long,
                context.UtcTime,
                confidence,
                market.LastPrice,
                stopPrice,
                takeProfitPrice,
                $"ORB breakout; RVOL={feature.RelativeVolume:F2}; VWAP={feature.Vwap:F4}");
        }
    }

    private decimal ComputeConfidence(
        decimal relativeVolume,
        decimal price,
        decimal breakoutLevel)
    {
        var volumeBoost = Math.Min(
            0.25m,
            Math.Max(0m, relativeVolume - _config.MinimumRelativeVolume) * 0.10m);

        var breakoutStrength = breakoutLevel > 0m
            ? (price - breakoutLevel) / breakoutLevel
            : 0m;

        var breakoutBoost = Math.Min(
            0.20m,
            Math.Max(0m, breakoutStrength) * 10m);

        return Math.Clamp(0.55m + volumeBoost + breakoutBoost, 0m, 0.95m);
    }

    private static void ValidateConfig(OpeningRangeMomentumConfig config)
    {
        if (config.MinimumPrice <= 0m ||
            config.MaximumPrice <= config.MinimumPrice ||
            config.MinimumRelativeVolume <= 0m ||
            config.BreakoutBufferPercent < 0m ||
            config.MaximumSignalSpreadPercent < 0m ||
            config.StopAtrMultiple <= 0m ||
            config.MinimumStopDistancePercent <= 0m ||
            config.TakeProfitRiskReward <= 0m ||
            config.MinimumSignalInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(config), "Strategy configuration contains invalid values.");
        }
    }
}
