using GE360.Trading.Domain;
using GE360.Trading.Execution;
using GE360.Trading.Features;
using GE360.Trading.LeanAdapter;
using GE360.Trading.Protection;
using GE360.Trading.Recovery;
using GE360.Trading.Risk;
using GE360.Trading.Strategies;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Orders.Slippage;

namespace GE360.Trading.LeanAlgorithm;

/// <summary>
/// Reference paper/backtest wiring for the first GE360 intraday strategy.
/// Live submission remains disabled in LeanExecutionAdapter.
/// </summary>
public sealed class Ge360OpeningRangePaperAlgorithm : QCAlgorithm
{
    private const string AssetTicker = "SPY";
    private const decimal ResearchFeePerOrderUsd = 0.50m;
    private const decimal ResearchSlippagePercent = 0.0005m;
    private static readonly TimeSpan IntradayFlattenTime = new(15, 55, 0);

    private Symbol _symbol = null!;
    private IntradayFeatureEngine _features = null!;
    private OpeningRangeMomentumStrategy _strategy = null!;
    private ProtectionEngine _protection = null!;
    private TradeApprovalService _approval = null!;
    private LeanExecutionAdapter _execution = null!;
    private PositionProtectionMonitor _positionProtection = null!;
    private LeanForwardPaperRecorder? _forwardPaperRecorder;
    private LeanRecoveryCoordinator? _recoveryCoordinator;

    private decimal _peakEquity;
    private decimal _dayStartEquity;
    private DateTime _equitySessionDate;

    public override void Initialize()
    {
        SetStartDate(2013, 10, 7);
        SetEndDate(2013, 10, 11);
        SetCash(100_000);
        SetTimeZone(TimeZones.NewYork);

        var equity = AddEquity(AssetTicker, Resolution.Minute);
        equity.SetFeeModel(new ConstantFeeModel(ResearchFeePerOrderUsd));
        equity.SetSlippageModel(new ConstantSlippageModel(ResearchSlippagePercent));

        _symbol = equity.Symbol;
        SetBenchmark(_symbol);

        _features = new IntradayFeatureEngine(AssetTicker);
        _strategy = new OpeningRangeMomentumStrategy();
        _protection = new ProtectionEngine(ProtectionConfig.ConservativePaperDefaults);
        _approval = new TradeApprovalService(
            _protection,
            new DefaultPreTradeRiskGate(RiskLimits.ConservativePaperDefaults));

        var configuredBrokerage = Config.Get("live-mode-brokerage");
        var isLeanPaperRuntime =
            LiveMode &&
            string.Equals(
                configuredBrokerage,
                "PaperBrokerage",
                StringComparison.OrdinalIgnoreCase);

        _execution = new LeanExecutionAdapter(
            this,
            new LeanExecutionOptions(
                EnableLiveSubmission: false,
                AllowPaperBrokerageSubmission: isLeanPaperRuntime,
                Asynchronous: false,
                BlockWhenMarketClosed: true));

        _positionProtection = new PositionProtectionMonitor();

        if (isLeanPaperRuntime)
        {
            var storePath = Environment.GetEnvironmentVariable(
                LeanForwardPaperRecorder.StorePathEnvironmentVariable);

            _forwardPaperRecorder = new LeanForwardPaperRecorder(
                storePath,
                message => Debug(message));

            Debug(
                $"GE360 forward-paper recorder enabled: {_forwardPaperRecorder.StorePath}");

            var recoveryPath = Environment.GetEnvironmentVariable(
                LeanRecoveryCoordinator.CheckpointPathEnvironmentVariable);

            _recoveryCoordinator = new LeanRecoveryCoordinator(
                recoveryPath,
                log: message => Debug(message));

            Debug(
                $"GE360 recovery checkpoint enabled: {_recoveryCoordinator.CheckpointPath}");
        }

        _peakEquity = Portfolio.TotalPortfolioValue;
        _dayStartEquity = Portfolio.TotalPortfolioValue;
        _equitySessionDate = Time.Date;
    }

    public override void OnData(Slice data)
    {
        if (!data.Bars.TryGetValue(_symbol, out var bar))
        {
            return;
        }

        try
        {
            var featureOutput = _features.Update(new IntradayBar(
                AssetTicker,
                UtcTime,
                bar.EndTime,
                bar.Open,
                bar.High,
                bar.Low,
                bar.Close,
                bar.Volume));

            var security = Securities[_symbol];
            var lastPrice = security.Price > 0m
                ? security.Price
                : bar.Close;
            var bidPrice = security.BidPrice > 0m
                ? security.BidPrice
                : lastPrice;
            var askPrice = security.AskPrice > 0m
                ? security.AskPrice
                : lastPrice;

            var market = (featureOutput?.Market ??
                new MarketSnapshot(
                    AssetTicker,
                    UtcTime,
                    lastPrice,
                    bidPrice,
                    askPrice,
                    bar.Volume,
                    null)) with
            {
                LastPrice = lastPrice,
                BidPrice = bidPrice,
                AskPrice = askPrice
            };

            if (!EnsureRecoveryReady(market))
            {
                return;
            }

            if (featureOutput is null)
            {
                return;
            }

            var portfolio = BuildPortfolioSnapshot(market);

            // Intraday invariant: no new risk after 15:55 New York time.
            // Any open position is flattened through the same GE360 approval/execution path.
            if (Time.TimeOfDay >= IntradayFlattenTime)
            {
                if (portfolio.Positions.ContainsKey(AssetTicker))
                {
                    var flattenSignal = SignalIntent.Create(
                        "intraday-session-control",
                        AssetTicker,
                        SignalDirection.Flat,
                        UtcTime,
                        1m,
                        market.LastPrice,
                        null,
                        null,
                        "intraday-flatten");

                    ProcessSignal(flattenSignal, market, portfolio);
                }

                return;
            }

            var protectiveExit = _positionProtection.Evaluate(
                market,
                portfolio,
                UtcTime);

            if (protectiveExit is not null)
            {
                ProcessSignal(protectiveExit, market, portfolio);
                return;
            }

            var context = new StrategyContext(
                UtcTime,
                new Dictionary<string, MarketSnapshot>
                {
                    [AssetTicker] = market
                },
                new Dictionary<string, StrategyFeatures>
                {
                    [AssetTicker] = featureOutput.Features
                },
                portfolio);

            foreach (var signal in _strategy.Evaluate(context))
            {
                ProcessSignal(signal, market, portfolio);
            }
        }
        finally
        {
            _forwardPaperRecorder?.ObserveBar(
                UtcTime,
                bar.EndTime,
                Portfolio.TotalPortfolioValue,
                Portfolio.Invested);

            if (_recoveryCoordinator?.StartupComplete == true &&
                !_recoveryCoordinator.PersistRuntime(
                    this,
                    gracefulShutdown: false))
            {
                _forwardPaperRecorder?.RecordStructuralFailure(
                    "recovery-checkpoint-persistence-failed");
            }
        }
    }

    public override void OnOrderEvent(OrderEvent orderEvent)
    {
        _forwardPaperRecorder?.ObserveOrderEvent(orderEvent);

        if (_recoveryCoordinator?.StartupComplete == true &&
            !_recoveryCoordinator.PersistRuntime(
                this,
                gracefulShutdown: false))
        {
            _forwardPaperRecorder?.RecordStructuralFailure(
                "recovery-checkpoint-persistence-failed-after-order-event");
        }
    }

    public override void OnEndOfAlgorithm()
    {
        if (_recoveryCoordinator is not null &&
            !_recoveryCoordinator.PersistRuntime(
                this,
                gracefulShutdown: true))
        {
            _forwardPaperRecorder?.RecordStructuralFailure(
                "recovery-checkpoint-persistence-failed-at-shutdown");
        }

        _forwardPaperRecorder?.FinalizeAtShutdown(
            UtcTime,
            Time,
            Portfolio.TotalPortfolioValue,
            Portfolio.Invested);

        if (Portfolio.Invested)
        {
            throw new InvalidOperationException(
                "GE360 intraday invariant violated: algorithm ended with an open position.");
        }
    }

    private bool EnsureRecoveryReady(
        MarketSnapshot currentMarket)
    {
        if (_recoveryCoordinator is null)
        {
            return true;
        }

        if (_recoveryCoordinator.StartupComplete &&
            _recoveryCoordinator.PersistenceHealthy)
        {
            return true;
        }

        RecoveryReconciliationResult result;

        if (!_recoveryCoordinator.StartupComplete)
        {
            result = _recoveryCoordinator.ReconcileStartup(
                this,
                _positionProtection);
        }
        else
        {
            var actual = _recoveryCoordinator.Capture(this);

            if (actual.IsFlat &&
                !actual.HasOpenOrders &&
                _recoveryCoordinator.PersistRuntime(
                    this,
                    gracefulShutdown: false))
            {
                Debug(
                    "GE360 recovery checkpoint persistence restored while flat.");
                return false;
            }

            result = RecoveryReconciliationResult.Reduce(
                actual,
                new[]
                {
                    "CHECKPOINT_PERSISTENCE_UNHEALTHY",
                    _recoveryCoordinator.LastError ?? "unknown"
                });
        }

        Debug(
            $"GE360 RECOVERY {result.Mode}: {string.Join(" | ", result.Reasons)}");

        if (result.RequiresCancelOpenOrders)
        {
            foreach (var order in Transactions.GetOpenOrders())
            {
                Transactions.CancelOrder(
                    order.Id,
                    "GE360 startup recovery cancel");
            }

            return false;
        }

        foreach (var symbol in result.SymbolsToFlatten)
        {
            var security = Securities.Values.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Symbol.Value,
                    symbol,
                    StringComparison.OrdinalIgnoreCase));

            if (security is null || security.Price <= 0m)
            {
                _forwardPaperRecorder?.RecordStructuralFailure(
                    $"recovery-unable-to-price-position:{symbol}");

                Debug(
                    $"GE360 RECOVERY cannot reduce {symbol}: active security/price unavailable.");
                continue;
            }

            var recoveryMarket =
                string.Equals(
                    symbol,
                    currentMarket.Symbol,
                    StringComparison.OrdinalIgnoreCase)
                    ? currentMarket
                    : new MarketSnapshot(
                        symbol,
                        UtcTime,
                        security.Price,
                        security.BidPrice > 0m
                            ? security.BidPrice
                            : security.Price,
                        security.AskPrice > 0m
                            ? security.AskPrice
                            : security.Price,
                        security.Volume,
                        null);

            var recoveryPortfolio =
                BuildPortfolioSnapshot(recoveryMarket);

            var flattenSignal = SignalIntent.Create(
                "startup-reconciliation",
                symbol,
                SignalDirection.Flat,
                UtcTime,
                1m,
                recoveryMarket.LastPrice,
                null,
                null,
                "startup-reconciliation-flat");

            ProcessSignal(
                flattenSignal,
                recoveryMarket,
                recoveryPortfolio);
        }

        // Even a successful synchronization consumes this bar. The strategy
        // cannot open new risk on the same time step as startup reconciliation.
        return false;
    }

    private void ProcessSignal(
        SignalIntent signal,
        MarketSnapshot market,
        PortfolioSnapshot portfolio)
    {
        var approval = _approval.Evaluate(signal, market, portfolio);
        if (!approval.Approved || approval.Order is null)
        {
            Debug($"GE360 REJECT {signal.Symbol}: {approval.Code} - {approval.Reason}");
            return;
        }

        var execution = _execution.Submit(approval.Order);
        if (!execution.Submitted)
        {
            Debug($"GE360 EXEC REJECT {signal.Symbol}: {execution.Code} - {execution.Reason}");

            if (IsStructuralExecutionFailure(execution.Code))
            {
                _forwardPaperRecorder?.RecordStructuralFailure(
                    $"{execution.Code}: {execution.Reason}");
            }

            if (approval.Order.IsRiskReducing)
            {
                _positionProtection.MarkExitSubmissionFailed(signal.Symbol);
            }

            return;
        }

        if (approval.Order.IsRiskReducing)
        {
            Debug($"GE360 EXIT submitted {signal.Symbol} order={execution.LeanOrderId} reason={signal.Reason}");
        }
        else
        {
            _positionProtection.RegisterEntry(approval.Order);
            _recoveryCoordinator?.RegisterEntry(approval.Order);
            Debug($"GE360 ENTRY submitted {signal.Symbol} qty={approval.Order.Quantity:F4} order={execution.LeanOrderId}");
        }
    }

    private static bool IsStructuralExecutionFailure(string code)
        => code is
            "LEAN_ORDER_REJECTED" or
            "LEAN_EXECUTION_ERROR" or
            "PAPER_SUBMISSION_DISABLED" or
            "LIVE_SUBMISSION_DISABLED" or
            "UNKNOWN_SYMBOL" or
            "SECURITY_NOT_SUBSCRIBED" or
            "MARKET_CLOSED" or
            "NO_POSITION_TO_REDUCE";

    private PortfolioSnapshot BuildPortfolioSnapshot(
        MarketSnapshot market)
    {
        var equity = Portfolio.TotalPortfolioValue;

        if (Time.Date != _equitySessionDate)
        {
            _equitySessionDate = Time.Date;
            _dayStartEquity = equity;
        }

        _peakEquity = Math.Max(
            _peakEquity,
            equity);

        var positions = new Dictionary<string, PositionSnapshot>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var security in Securities.Values)
        {
            var holdings = security.Holdings;

            if (holdings.Quantity == 0m)
            {
                continue;
            }

            var marketPrice =
                string.Equals(
                    security.Symbol.Value,
                    market.Symbol,
                    StringComparison.OrdinalIgnoreCase)
                    ? market.LastPrice
                    : security.Price > 0m
                        ? security.Price
                        : holdings.AveragePrice;

            positions[security.Symbol.Value] =
                new PositionSnapshot(
                    security.Symbol.Value,
                    holdings.Quantity,
                    holdings.AveragePrice,
                    marketPrice);
        }

        var recoveryReducing =
            _recoveryCoordinator is not null &&
            _recoveryCoordinator.TradingState ==
                TradingState.Reducing;

        var tradingState = recoveryReducing
            ? TradingState.Reducing
            : _protection.IsHalted
                ? TradingState.Halted
                : TradingState.PaperOnly;

        return new PortfolioSnapshot(
            UtcTime,
            tradingState,
            equity,
            _peakEquity,
            _dayStartEquity,
            Portfolio.Cash,
            positions);
    }}
