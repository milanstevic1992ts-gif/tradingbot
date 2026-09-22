using GE360.Trading.Domain;
using GE360.Trading.Execution;
using GE360.Trading.Risk;

namespace GE360.Trading.Protection;

/// <summary>
/// Software protection monitor for paper/backtest operation.
/// Live trading remains disabled by default because broker-native protective orders
/// require brokerage-specific handling.
/// </summary>
public sealed class PositionProtectionMonitor
{
    private readonly Dictionary<string, ProtectionState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    public void RegisterEntry(ApprovedOrderIntent order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.IsRiskReducing || order.Quantity == 0m)
        {
            return;
        }

        if (order.StopPrice is null && order.TakeProfitPrice is null)
        {
            return;
        }

        _states[order.Symbol] = new ProtectionState(
            order.StrategyId,
            order.Quantity > 0m,
            order.StopPrice,
            order.TakeProfitPrice,
            false);
    }

    public void Restore(
        string strategyId,
        string symbol,
        decimal quantity,
        decimal? stopPrice,
        decimal? takeProfitPrice)
    {
        if (string.IsNullOrWhiteSpace(strategyId))
        {
            throw new ArgumentException(
                "Strategy id is required for protection recovery.",
                nameof(strategyId));
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException(
                "Symbol is required for protection recovery.",
                nameof(symbol));
        }

        if (quantity == 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Recovered protection requires a non-zero position.");
        }

        if (stopPrice is null && takeProfitPrice is null)
        {
            throw new ArgumentException(
                "Recovered protection requires a stop or take-profit level.");
        }

        _states[symbol.Trim().ToUpperInvariant()] = new ProtectionState(
            strategyId.Trim(),
            quantity > 0m,
            stopPrice,
            takeProfitPrice,
            false);
    }

    public SignalIntent? Evaluate(
        MarketSnapshot market,
        PortfolioSnapshot portfolio,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(portfolio);

        if (!_states.TryGetValue(market.Symbol, out var state))
        {
            return null;
        }

        if (!portfolio.Positions.TryGetValue(market.Symbol, out var position) ||
            position.Quantity == 0m)
        {
            _states.Remove(market.Symbol);
            return null;
        }

        if (state.ExitPending)
        {
            return null;
        }

        var stopHit = state.IsLong
            ? state.StopPrice.HasValue && market.LastPrice <= state.StopPrice.Value
            : state.StopPrice.HasValue && market.LastPrice >= state.StopPrice.Value;

        var takeProfitHit = state.IsLong
            ? state.TakeProfitPrice.HasValue && market.LastPrice >= state.TakeProfitPrice.Value
            : state.TakeProfitPrice.HasValue && market.LastPrice <= state.TakeProfitPrice.Value;

        if (!stopHit && !takeProfitHit)
        {
            return null;
        }

        _states[market.Symbol] = state with { ExitPending = true };

        return SignalIntent.Create(
            state.StrategyId,
            market.Symbol,
            SignalDirection.Flat,
            utcNow,
            1m,
            market.LastPrice,
            null,
            null,
            stopHit ? "protective-stop-triggered" : "take-profit-triggered");
    }

    public void MarkExitSubmissionFailed(string symbol)
    {
        if (_states.TryGetValue(symbol, out var state))
        {
            _states[symbol] = state with { ExitPending = false };
        }
    }

    public void Clear(string symbol)
        => _states.Remove(symbol);

    private sealed record ProtectionState(
        string StrategyId,
        bool IsLong,
        decimal? StopPrice,
        decimal? TakeProfitPrice,
        bool ExitPending);
}
