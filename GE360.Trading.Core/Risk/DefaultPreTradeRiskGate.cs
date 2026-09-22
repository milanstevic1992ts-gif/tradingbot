using GE360.Trading.Domain;

namespace GE360.Trading.Risk;

/// <summary>
/// Deterministic fail-closed gate between strategy intent and executable quantity.
/// It never submits orders; it only approves or rejects a proposed signal.
/// </summary>
public sealed class DefaultPreTradeRiskGate : IPreTradeRiskGate
{
    private readonly RiskLimits _limits;

    public DefaultPreTradeRiskGate(RiskLimits limits)
    {
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        ValidateLimits(_limits);
    }

    public RiskDecision Evaluate(
        SignalIntent signal,
        MarketSnapshot market,
        PortfolioSnapshot portfolio)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(portfolio);

        try
        {
            return EvaluateCore(signal, market, portfolio);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return RiskDecision.Reject(
                "RISK_GATE_ERROR",
                $"Risk evaluation failed closed: {exception.GetType().Name}.");
        }
    }

    private RiskDecision EvaluateCore(
        SignalIntent signal,
        MarketSnapshot market,
        PortfolioSnapshot portfolio)
    {
        if (!string.Equals(signal.Symbol, market.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            return RiskDecision.Reject("SYMBOL_MISMATCH", "Signal and market snapshot symbols differ.");
        }

        if (portfolio.Equity <= 0m)
        {
            return RiskDecision.Reject("INVALID_EQUITY", "Portfolio equity must be positive.", true);
        }

        var existingPosition = portfolio.Positions.TryGetValue(signal.Symbol, out var position)
            ? position
            : null;

        // Risk-reducing exits must remain possible even after a global halt.
        if (signal.Direction == SignalDirection.Flat)
        {
            if (existingPosition is null || existingPosition.Quantity == 0m)
            {
                return RiskDecision.Reject("NO_POSITION_TO_REDUCE", "Flat signal has no open position to reduce.");
            }

            return RiskDecision.Approve(-existingPosition.Quantity);
        }

        if (portfolio.TradingState == TradingState.Halted)
        {
            return RiskDecision.Reject("TRADING_HALTED", "New risk is blocked while trading is halted.");
        }

        if (portfolio.TradingState == TradingState.Reducing)
        {
            return RiskDecision.Reject("REDUCING_ONLY", "Only risk-reducing exits are allowed.");
        }

        if (signal.Direction == SignalDirection.Short && !_limits.AllowShort)
        {
            return RiskDecision.Reject("SHORT_DISABLED", "Short selling is disabled by hard risk limits.");
        }

        var staleDecision = ValidateFreshness(signal, market, portfolio);
        if (staleDecision is not null)
        {
            return staleDecision;
        }

        if (portfolio.DailyLossPercent >= _limits.MaxDailyLossPercent)
        {
            return RiskDecision.Reject(
                "MAX_DAILY_LOSS",
                "Daily loss limit reached.",
                true);
        }

        if (portfolio.DrawdownPercent >= _limits.MaxDrawdownPercent)
        {
            return RiskDecision.Reject(
                "MAX_DRAWDOWN",
                "Portfolio drawdown limit reached.",
                true);
        }

        if (market.LastPrice <= 0m)
        {
            return RiskDecision.Reject("INVALID_PRICE", "Market price must be positive.");
        }

        if (market.SpreadPercent > _limits.MaxSpreadPercent)
        {
            return RiskDecision.Reject("SPREAD_TOO_WIDE", "Current spread exceeds the configured maximum.");
        }

        var isNewPosition = existingPosition is null || existingPosition.Quantity == 0m;
        if (isNewPosition && portfolio.OpenPositionCount >= _limits.MaxOpenPositions)
        {
            return RiskDecision.Reject("MAX_OPEN_POSITIONS", "Maximum number of open positions reached.");
        }

        var stopDecision = ValidateStop(signal, market);
        if (stopDecision is not null)
        {
            return stopDecision;
        }

        var quantity = SizePosition(signal, market, portfolio, existingPosition);
        if (quantity == 0m)
        {
            return RiskDecision.Reject("ZERO_SIZE", "No risk capacity is available for this signal.");
        }

        return RiskDecision.Approve(quantity);
    }

    private RiskDecision? ValidateFreshness(
        SignalIntent signal,
        MarketSnapshot market,
        PortfolioSnapshot portfolio)
    {
        if (signal.SignalTimeUtc > portfolio.UtcTime + TimeSpan.FromSeconds(5))
        {
            return RiskDecision.Reject("FUTURE_SIGNAL", "Signal timestamp is in the future.");
        }

        if (market.UtcTime > portfolio.UtcTime + TimeSpan.FromSeconds(5))
        {
            return RiskDecision.Reject("FUTURE_MARKET_DATA", "Market snapshot timestamp is in the future.");
        }

        if (portfolio.UtcTime - signal.SignalTimeUtc > _limits.MaxSignalAge)
        {
            return RiskDecision.Reject("STALE_SIGNAL", "Signal is older than the maximum allowed age.");
        }

        if (portfolio.UtcTime - market.UtcTime > _limits.MaxSignalAge)
        {
            return RiskDecision.Reject("STALE_MARKET_DATA", "Market data is older than the maximum allowed age.");
        }

        return null;
    }

    private RiskDecision? ValidateStop(SignalIntent signal, MarketSnapshot market)
    {
        if (!_limits.RequireStopLoss && signal.SuggestedStopPrice is null)
        {
            return null;
        }

        if (signal.SuggestedStopPrice is null || signal.SuggestedStopPrice <= 0m)
        {
            return RiskDecision.Reject("STOP_REQUIRED", "A valid protective stop is required.");
        }

        var stop = signal.SuggestedStopPrice.Value;
        if (signal.Direction == SignalDirection.Long && stop >= market.LastPrice)
        {
            return RiskDecision.Reject("INVALID_LONG_STOP", "Long stop must be below market price.");
        }

        if (signal.Direction == SignalDirection.Short && stop <= market.LastPrice)
        {
            return RiskDecision.Reject("INVALID_SHORT_STOP", "Short stop must be above market price.");
        }

        var distancePercent = Math.Abs(market.LastPrice - stop) / market.LastPrice;
        if (distancePercent < _limits.MinStopDistancePercent)
        {
            return RiskDecision.Reject("STOP_TOO_TIGHT", "Stop distance is below the configured minimum.");
        }

        if (distancePercent > _limits.MaxStopDistancePercent)
        {
            return RiskDecision.Reject("STOP_TOO_WIDE", "Stop distance exceeds the configured maximum.");
        }

        return null;
    }

    private decimal SizePosition(
        SignalIntent signal,
        MarketSnapshot market,
        PortfolioSnapshot portfolio,
        PositionSnapshot? existingPosition)
    {
        var price = market.LastPrice;
        var direction = signal.Direction == SignalDirection.Long ? 1m : -1m;

        decimal riskQuantity;
        if (signal.SuggestedStopPrice is not null)
        {
            var stopDistance = Math.Abs(price - signal.SuggestedStopPrice.Value);
            if (stopDistance <= 0m)
            {
                return 0m;
            }

            var riskBudget = portfolio.Equity * _limits.MaxRiskPerTradePercent;
            riskQuantity = riskBudget / stopDistance;
        }
        else
        {
            riskQuantity = decimal.MaxValue;
        }

        var maxPositionNotional = portfolio.Equity * _limits.MaxPositionPercent;
        var existingNotional = existingPosition?.Notional ?? 0m;
        var positionCapacity = Math.Max(0m, maxPositionNotional - existingNotional) / price;

        var grossCapacityNotional = Math.Max(
            0m,
            portfolio.Equity * _limits.MaxTotalExposurePercent - portfolio.GrossExposure);
        var exposureCapacity = grossCapacityNotional / price;

        var notionalCapacity = _limits.MaxOrderNotional / price;

        var absoluteQuantity = Math.Min(
            riskQuantity,
            Math.Min(positionCapacity, Math.Min(exposureCapacity, notionalCapacity)));

        if (absoluteQuantity <= 0m || absoluteQuantity == decimal.MaxValue)
        {
            return 0m;
        }

        return direction * absoluteQuantity;
    }

    private static void ValidateLimits(RiskLimits limits)
    {
        if (limits.MaxRiskPerTradePercent <= 0m ||
            limits.MaxPositionPercent <= 0m ||
            limits.MaxTotalExposurePercent <= 0m ||
            limits.MaxDailyLossPercent <= 0m ||
            limits.MaxDrawdownPercent <= 0m ||
            limits.MaxOpenPositions <= 0 ||
            limits.MaxSpreadPercent < 0m ||
            limits.MaxOrderNotional <= 0m ||
            limits.MaxSignalAge <= TimeSpan.Zero ||
            limits.MinStopDistancePercent < 0m ||
            limits.MaxStopDistancePercent <= limits.MinStopDistancePercent)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "Risk limits contain invalid values.");
        }
    }
}
