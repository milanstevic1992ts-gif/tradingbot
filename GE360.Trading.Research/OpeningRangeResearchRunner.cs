using GE360.Trading.Domain;
using GE360.Trading.Execution;
using GE360.Trading.Features;
using GE360.Trading.Protection;
using GE360.Trading.Risk;
using GE360.Trading.Strategies;

namespace GE360.Trading.Research;

/// <summary>
/// Deterministic research harness that reuses the production strategy/risk/protection stack.
/// Fills are simulated with an explicit fee/slippage model.
/// </summary>
public sealed class OpeningRangeResearchRunner
{
    private readonly TimeSpan _flattenTimeLocal;

    public OpeningRangeResearchRunner(TimeSpan? flattenTimeLocal = null)
    {
        _flattenTimeLocal = flattenTimeLocal ?? new TimeSpan(15, 55, 0);

        if (_flattenTimeLocal <= TimeSpan.Zero ||
            _flattenTimeLocal >= TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(flattenTimeLocal));
        }
    }

    public ResearchBacktestResult Run(
        IEnumerable<IntradayBar> sourceBars,
        decimal initialEquity = 100_000m,
        ResearchCostModel? costs = null,
        OpeningRangeMomentumConfig? strategyConfig = null)
    {
        if (initialEquity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(initialEquity));
        }

        var bars = sourceBars
            .OrderBy(x => x.UtcTime)
            .ToArray();

        if (bars.Length == 0)
        {
            return EmptyResult(initialEquity);
        }

        var symbols = bars
            .Select(x => x.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (symbols.Length != 1)
        {
            throw new ArgumentException("Research runner accepts one symbol per run.", nameof(sourceBars));
        }

        var symbol = symbols[0];
        var costModel = costs ?? ResearchCostModel.DeterministicSmokeDefaults;
        var strategy = new OpeningRangeMomentumStrategy(strategyConfig);
        var protection = new ProtectionEngine(ProtectionConfig.ConservativePaperDefaults);
        var approval = new TradeApprovalService(
            protection,
            new DefaultPreTradeRiskGate(RiskLimits.ConservativePaperDefaults));

        var trades = new List<ResearchTrade>();
        var realizedEquity = initialEquity;
        var peakEquity = initialEquity;
        PositionState? position = null;

        foreach (var session in bars.GroupBy(x => x.ExchangeLocalTime.Date).OrderBy(x => x.Key))
        {
            var sessionBars = session.OrderBy(x => x.UtcTime).ToArray();
            var featureEngine = new IntradayFeatureEngine(symbol);
            var dayStartEquity = realizedEquity;

            foreach (var bar in sessionBars)
            {
                var markEquity = realizedEquity + UnrealizedPnl(position, bar.Close);
                peakEquity = Math.Max(peakEquity, markEquity);

                if (position is not null)
                {
                    var exit = ResolveExit(position, bar);
                    if (exit is not null)
                    {
                        var trade = ClosePosition(
                            position,
                            bar.UtcTime,
                            exit.Value.Price,
                            exit.Value.Reason,
                            costModel);

                        trades.Add(trade);
                        realizedEquity += trade.NetPnl;
                        protection.RecordTradeOutcome(new TradeOutcome(
                            trade.StrategyId,
                            trade.Symbol,
                            trade.ExitTimeUtc,
                            trade.NetPnl));
                        position = null;

                        if (bar.ExchangeLocalTime.TimeOfDay >= _flattenTimeLocal)
                        {
                            continue;
                        }
                    }
                }

                var output = featureEngine.Update(bar);
                if (output is null ||
                    position is not null ||
                    bar.ExchangeLocalTime.TimeOfDay >= _flattenTimeLocal)
                {
                    continue;
                }

                var portfolio = BuildPortfolio(
                    bar.UtcTime,
                    realizedEquity,
                    peakEquity,
                    dayStartEquity,
                    position,
                    bar.Close,
                    protection.IsHalted);

                var context = new StrategyContext(
                    bar.UtcTime,
                    new Dictionary<string, MarketSnapshot>
                    {
                        [symbol] = output.Market
                    },
                    new Dictionary<string, StrategyFeatures>
                    {
                        [symbol] = output.Features
                    },
                    portfolio);

                var signal = strategy.Evaluate(context).FirstOrDefault();
                if (signal is null)
                {
                    continue;
                }

                var result = approval.Evaluate(signal, output.Market, portfolio);
                if (!result.Approved || result.Order is null || result.Order.Quantity == 0m)
                {
                    continue;
                }

                var order = result.Order;
                var fill = costModel.ApplyFillPrice(bar.Close, order.Quantity);

                position = new PositionState(
                    order.StrategyId,
                    order.Symbol,
                    order.Quantity,
                    bar.UtcTime,
                    bar.Close,
                    fill,
                    order.StopPrice,
                    order.TakeProfitPrice,
                    costModel.FeePerOrder);
            }

            if (position is not null)
            {
                var last = sessionBars[^1];
                var trade = ClosePosition(
                    position,
                    last.UtcTime,
                    last.Close,
                    "session-end-forced-flat",
                    costModel);

                trades.Add(trade);
                realizedEquity += trade.NetPnl;
                protection.RecordTradeOutcome(new TradeOutcome(
                    trade.StrategyId,
                    trade.Symbol,
                    trade.ExitTimeUtc,
                    trade.NetPnl));
                position = null;
            }
        }

        var sessions = bars
            .Select(x => x.ExchangeLocalTime.Date)
            .Distinct()
            .Count();

        var metrics = ResearchMetrics.Calculate(trades, initialEquity);

        return new ResearchBacktestResult(
            bars.Length,
            sessions,
            trades,
            metrics,
            ResearchValidation.Assess(sessions, trades.Count));
    }

    private (decimal Price, string Reason)? ResolveExit(
        PositionState position,
        IntradayBar bar)
    {
        if (position.Quantity > 0m)
        {
            // Conservative same-bar ambiguity rule: stop is evaluated before target.
            if (position.StopPrice.HasValue && bar.Low <= position.StopPrice.Value)
            {
                return (position.StopPrice.Value, "protective-stop");
            }

            if (position.TakeProfitPrice.HasValue && bar.High >= position.TakeProfitPrice.Value)
            {
                return (position.TakeProfitPrice.Value, "take-profit");
            }
        }
        else
        {
            if (position.StopPrice.HasValue && bar.High >= position.StopPrice.Value)
            {
                return (position.StopPrice.Value, "protective-stop");
            }

            if (position.TakeProfitPrice.HasValue && bar.Low <= position.TakeProfitPrice.Value)
            {
                return (position.TakeProfitPrice.Value, "take-profit");
            }
        }

        if (bar.ExchangeLocalTime.TimeOfDay >= _flattenTimeLocal)
        {
            return (bar.Close, "intraday-flatten");
        }

        return null;
    }

    private static ResearchTrade ClosePosition(
        PositionState position,
        DateTime exitTimeUtc,
        decimal referenceExitPrice,
        string reason,
        ResearchCostModel costs)
    {
        var exitQuantity = -position.Quantity;
        var exitFill = costs.ApplyFillPrice(referenceExitPrice, exitQuantity);

        return new ResearchTrade(
            position.StrategyId,
            position.Symbol,
            position.EntryTimeUtc,
            exitTimeUtc,
            position.Quantity,
            position.ReferenceEntryPrice,
            referenceExitPrice,
            position.FillEntryPrice,
            exitFill,
            position.EntryFee + costs.FeePerOrder,
            reason);
    }

    private static PortfolioSnapshot BuildPortfolio(
        DateTime utcTime,
        decimal realizedEquity,
        decimal peakEquity,
        decimal dayStartEquity,
        PositionState? position,
        decimal marketPrice,
        bool halted)
    {
        var positions = new Dictionary<string, PositionSnapshot>();
        if (position is not null)
        {
            positions[position.Symbol] = new PositionSnapshot(
                position.Symbol,
                position.Quantity,
                position.FillEntryPrice,
                marketPrice);
        }

        var equity = realizedEquity + UnrealizedPnl(position, marketPrice);

        return new PortfolioSnapshot(
            utcTime,
            halted ? TradingState.Halted : TradingState.PaperOnly,
            equity,
            peakEquity,
            dayStartEquity,
            realizedEquity,
            positions);
    }

    private static decimal UnrealizedPnl(PositionState? position, decimal marketPrice)
        => position is null
            ? 0m
            : (marketPrice - position.FillEntryPrice) * position.Quantity;

    private static ResearchBacktestResult EmptyResult(decimal initialEquity)
    {
        var trades = Array.Empty<ResearchTrade>();
        return new ResearchBacktestResult(
            0,
            0,
            trades,
            ResearchMetrics.Calculate(trades, initialEquity),
            ResearchValidation.Assess(0, 0));
    }

    private sealed record PositionState(
        string StrategyId,
        string Symbol,
        decimal Quantity,
        DateTime EntryTimeUtc,
        decimal ReferenceEntryPrice,
        decimal FillEntryPrice,
        decimal? StopPrice,
        decimal? TakeProfitPrice,
        decimal EntryFee);
}
