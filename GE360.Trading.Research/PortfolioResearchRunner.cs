using GE360.Trading.Domain;
using GE360.Trading.Execution;
using GE360.Trading.Features;
using GE360.Trading.Protection;
using GE360.Trading.Risk;
using GE360.Trading.Strategies;

namespace GE360.Trading.Research;

/// <summary>
/// Multi-symbol deterministic research harness.
/// Reuses GE360 strategy, approval, protection and execution-guard code with shared portfolio risk.
/// </summary>
public sealed class PortfolioResearchRunner
{
    private readonly TimeSpan _flattenTimeLocal;
    private readonly ResearchMarketAssumptions _marketAssumptions;

    public PortfolioResearchRunner(
        TimeSpan? flattenTimeLocal = null,
        ResearchMarketAssumptions? marketAssumptions = null)
    {
        _flattenTimeLocal = flattenTimeLocal ?? new TimeSpan(15, 55, 0);
        _marketAssumptions = marketAssumptions ?? ResearchMarketAssumptions.ConservativeSmokeDefaults;

        if (_flattenTimeLocal <= TimeSpan.Zero ||
            _flattenTimeLocal >= TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(flattenTimeLocal));
        }
    }

    public PortfolioResearchResult Run(
        IEnumerable<IntradayBar> sourceBars,
        decimal initialEquity = 100_000m,
        ResearchCostModel? costs = null,
        OpeningRangeMomentumConfig? strategyConfig = null,
        bool datasetAdequate = false)
    {
        if (initialEquity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(initialEquity));
        }

        var bars = sourceBars
            .OrderBy(x => x.UtcTime)
            .ThenBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (bars.Length == 0)
        {
            var emptyTrades = Array.Empty<ResearchTrade>();
            return new PortfolioResearchResult(
                0,
                0,
                0,
                0,
                emptyTrades,
                ResearchMetrics.Calculate(emptyTrades, initialEquity),
                ResearchValidation.Assess(0, 0, datasetAdequate: datasetAdequate));
        }

        var symbols = bars
            .Select(x => x.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var calendarSessions = bars
            .Select(x => x.ExchangeLocalTime.Date)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var symbolSessionCount = bars
            .Select(x => $"{x.Symbol.ToUpperInvariant()}:{x.ExchangeLocalTime:yyyyMMdd}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var lastBarBySymbolSession = bars
            .GroupBy(x => $"{x.Symbol.ToUpperInvariant()}:{x.ExchangeLocalTime:yyyyMMdd}")
            .ToDictionary(
                group => group.Key,
                group => group.Max(x => x.UtcTime),
                StringComparer.OrdinalIgnoreCase);

        var featureEngines = symbols.ToDictionary(
            symbol => symbol,
            symbol => new IntradayFeatureEngine(symbol),
            StringComparer.OrdinalIgnoreCase);

        var strategy = new OpeningRangeMomentumStrategy(strategyConfig);
        var protection = new ProtectionEngine(ProtectionConfig.ConservativePaperDefaults);
        var approval = new TradeApprovalService(
            protection,
            new DefaultPreTradeRiskGate(RiskLimits.ConservativePaperDefaults));
        var executionGuard = new ExecutionGuard(ExecutionGuardConfig.ConservativePaperDefaults);
        var costModel = costs ?? ResearchCostModel.DeterministicSmokeDefaults;

        var positions = new Dictionary<string, PositionState>(StringComparer.OrdinalIgnoreCase);
        var lastPrices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var trades = new List<ResearchTrade>();

        var realizedEquity = initialEquity;
        var peakEquity = initialEquity;
        var currentSessionDate = DateTime.MinValue;
        var dayStartEquity = initialEquity;

        foreach (var bar in bars)
        {
            if (currentSessionDate != bar.ExchangeLocalTime.Date)
            {
                if (positions.Count != 0)
                {
                    throw new InvalidOperationException(
                        "Research session boundary reached with open positions.");
                }

                currentSessionDate = bar.ExchangeLocalTime.Date;
                dayStartEquity = realizedEquity;
            }

            lastPrices[bar.Symbol] = bar.Close;
            peakEquity = Math.Max(
                peakEquity,
                MarkEquity(realizedEquity, positions, lastPrices));

            var output = featureEngines[bar.Symbol].Update(bar);
            var market = _marketAssumptions.Apply(
                output?.Market ?? new MarketSnapshot(
                    bar.Symbol,
                    bar.UtcTime,
                    bar.Close,
                    bar.Close,
                    bar.Close,
                    bar.Volume,
                    null));

            var symbolSessionKey =
                $"{bar.Symbol.ToUpperInvariant()}:{bar.ExchangeLocalTime:yyyyMMdd}";
            var isLastBarOfSymbolSession =
                lastBarBySymbolSession[symbolSessionKey] == bar.UtcTime;

            var closedThisBar = false;

            if (positions.TryGetValue(bar.Symbol, out var position))
            {
                var exit = ResolveExit(position, bar, isLastBarOfSymbolSession);
                if (exit is not null)
                {
                    var portfolio = BuildPortfolio(
                        bar.UtcTime,
                        realizedEquity,
                        peakEquity,
                        dayStartEquity,
                        positions,
                        lastPrices,
                        protection.IsHalted);

                    var exitMarket = _marketAssumptions.AtPrice(
                        bar.Symbol,
                        bar.UtcTime,
                        exit.Value.Price,
                        bar.Volume,
                        market.Atr);

                    var flatSignal = SignalIntent.Create(
                        position.StrategyId,
                        bar.Symbol,
                        SignalDirection.Flat,
                        bar.UtcTime,
                        1m,
                        exit.Value.Price,
                        null,
                        null,
                        exit.Value.Reason);

                    var approvedExit = approval.Evaluate(
                        flatSignal,
                        exitMarket,
                        portfolio);

                    if (!approvedExit.Approved || approvedExit.Order is null)
                    {
                        throw new InvalidOperationException(
                            $"Risk-reducing exit was rejected: {approvedExit.Code}.");
                    }

                    var executionDecision = executionGuard.Evaluate(
                        approvedExit.Order,
                        exitMarket,
                        bar.UtcTime);

                    if (!executionDecision.Allowed)
                    {
                        throw new InvalidOperationException(
                            $"Risk-reducing execution was blocked: {executionDecision.Code}.");
                    }

                    executionGuard.RecordSubmission(approvedExit.Order, bar.UtcTime);

                    var trade = ClosePosition(
                        position,
                        bar.UtcTime,
                        exit.Value.Price,
                        exit.Value.Reason,
                        costModel);

                    trades.Add(trade);
                    realizedEquity += trade.NetPnl;
                    positions.Remove(bar.Symbol);
                    protection.RecordTradeOutcome(new TradeOutcome(
                        trade.StrategyId,
                        trade.Symbol,
                        trade.ExitTimeUtc,
                        trade.NetPnl));

                    closedThisBar = true;
                    peakEquity = Math.Max(
                        peakEquity,
                        MarkEquity(realizedEquity, positions, lastPrices));
                }
            }

            if (closedThisBar ||
                output is null ||
                positions.ContainsKey(bar.Symbol) ||
                bar.ExchangeLocalTime.TimeOfDay >= _flattenTimeLocal ||
                isLastBarOfSymbolSession)
            {
                continue;
            }

            var currentPortfolio = BuildPortfolio(
                bar.UtcTime,
                realizedEquity,
                peakEquity,
                dayStartEquity,
                positions,
                lastPrices,
                protection.IsHalted);

            var context = new StrategyContext(
                bar.UtcTime,
                new Dictionary<string, MarketSnapshot>(StringComparer.OrdinalIgnoreCase)
                {
                    [bar.Symbol] = market
                },
                new Dictionary<string, StrategyFeatures>(StringComparer.OrdinalIgnoreCase)
                {
                    [bar.Symbol] = output.Features
                },
                currentPortfolio);

            var signal = strategy.Evaluate(context).FirstOrDefault();
            if (signal is null)
            {
                continue;
            }

            var approvalResult = approval.Evaluate(
                signal,
                market,
                currentPortfolio);

            if (!approvalResult.Approved ||
                approvalResult.Order is null ||
                approvalResult.Order.Quantity == 0m)
            {
                continue;
            }

            var execution = executionGuard.Evaluate(
                approvalResult.Order,
                market,
                bar.UtcTime);

            if (!execution.Allowed)
            {
                continue;
            }

            executionGuard.RecordSubmission(approvalResult.Order, bar.UtcTime);

            var order = approvalResult.Order;
            var fillPrice = costModel.ApplyFillPrice(
                market.LastPrice,
                order.Quantity);

            positions[bar.Symbol] = new PositionState(
                order.StrategyId,
                order.Symbol,
                order.Quantity,
                bar.UtcTime,
                market.LastPrice,
                fillPrice,
                order.StopPrice,
                order.TakeProfitPrice,
                costModel.FeePerOrder);
        }

        if (positions.Count != 0)
        {
            throw new InvalidOperationException(
                "Portfolio research ended with open positions.");
        }

        var metrics = ResearchMetrics.Calculate(trades, initialEquity);

        return new PortfolioResearchResult(
            bars.Length,
            calendarSessions.Length,
            symbols.Length,
            symbolSessionCount,
            trades,
            metrics,
            ResearchValidation.Assess(
                calendarSessions.Length,
                trades.Count,
                datasetAdequate: datasetAdequate));
    }

    private (decimal Price, string Reason)? ResolveExit(
        PositionState position,
        IntradayBar bar,
        bool isLastBarOfSymbolSession)
    {
        if (position.Quantity > 0m)
        {
            if (position.StopPrice.HasValue &&
                bar.Low <= position.StopPrice.Value)
            {
                return (position.StopPrice.Value, "protective-stop");
            }

            if (position.TakeProfitPrice.HasValue &&
                bar.High >= position.TakeProfitPrice.Value)
            {
                return (position.TakeProfitPrice.Value, "take-profit");
            }
        }
        else
        {
            if (position.StopPrice.HasValue &&
                bar.High >= position.StopPrice.Value)
            {
                return (position.StopPrice.Value, "protective-stop");
            }

            if (position.TakeProfitPrice.HasValue &&
                bar.Low <= position.TakeProfitPrice.Value)
            {
                return (position.TakeProfitPrice.Value, "take-profit");
            }
        }

        if (bar.ExchangeLocalTime.TimeOfDay >= _flattenTimeLocal)
        {
            return (bar.Close, "intraday-flatten");
        }

        if (isLastBarOfSymbolSession)
        {
            return (bar.Close, "session-end-forced-flat");
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
        var exitFill = costs.ApplyFillPrice(
            referenceExitPrice,
            exitQuantity);

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
        IReadOnlyDictionary<string, PositionState> positions,
        IReadOnlyDictionary<string, decimal> lastPrices,
        bool halted)
    {
        var snapshots = new Dictionary<string, PositionSnapshot>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var pair in positions)
        {
            var marketPrice = lastPrices.TryGetValue(pair.Key, out var price)
                ? price
                : pair.Value.FillEntryPrice;

            snapshots[pair.Key] = new PositionSnapshot(
                pair.Key,
                pair.Value.Quantity,
                pair.Value.FillEntryPrice,
                marketPrice);
        }

        var equity = MarkEquity(realizedEquity, positions, lastPrices);
        var cash = realizedEquity -
                   positions.Values.Sum(x => x.Quantity * x.FillEntryPrice);

        return new PortfolioSnapshot(
            utcTime,
            halted ? TradingState.Halted : TradingState.PaperOnly,
            equity,
            peakEquity,
            dayStartEquity,
            cash,
            snapshots);
    }

    private static decimal MarkEquity(
        decimal realizedEquity,
        IReadOnlyDictionary<string, PositionState> positions,
        IReadOnlyDictionary<string, decimal> lastPrices)
    {
        var equity = realizedEquity;

        foreach (var pair in positions)
        {
            var position = pair.Value;
            var marketPrice = lastPrices.TryGetValue(pair.Key, out var price)
                ? price
                : position.FillEntryPrice;

            equity +=
                (marketPrice - position.FillEntryPrice) * position.Quantity -
                position.EntryFee;
        }

        return equity;
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
