using GE360.Trading.Domain;
using GE360.Trading.Execution;
using GE360.Trading.Features;
using GE360.Trading.LeanAdapter;
using GE360.Trading.Protection;
using GE360.Trading.Risk;
using GE360.Trading.Strategies;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data;
using QuantConnect.Data.Market;
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

        _execution = new LeanExecutionAdapter(
            this,
            new LeanExecutionOptions(
                EnableLiveSubmission: false,
                Asynchronous: false,
                BlockWhenMarketClosed: true));

        _positionProtection = new PositionProtectionMonitor();

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

        var featureOutput = _features.Update(new IntradayBar(
            AssetTicker,
            UtcTime,
            bar.EndTime,
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close,
            bar.Volume));

        if (featureOutput is null)
        {
            return;
        }

        var security = Securities[_symbol];
        var lastPrice = security.Price > 0m ? security.Price : bar.Close;
        var bidPrice = security.BidPrice > 0m ? security.BidPrice : lastPrice;
        var askPrice = security.AskPrice > 0m ? security.AskPrice : lastPrice;

        var market = featureOutput.Market with
        {
            LastPrice = lastPrice,
            BidPrice = bidPrice,
            AskPrice = askPrice
        };

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

    public override void OnEndOfAlgorithm()
    {
        if (Portfolio.Invested)
        {
            throw new InvalidOperationException(
                "GE360 intraday invariant violated: algorithm ended with an open position.");
        }
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
            Debug($"GE360 ENTRY submitted {signal.Symbol} qty={approval.Order.Quantity:F4} order={execution.LeanOrderId}");
        }
    }

    private PortfolioSnapshot BuildPortfolioSnapshot(MarketSnapshot market)
    {
        var equity = Portfolio.TotalPortfolioValue;

        if (Time.Date != _equitySessionDate)
        {
            _equitySessionDate = Time.Date;
            _dayStartEquity = equity;
        }

        _peakEquity = Math.Max(_peakEquity, equity);

        var positions = new Dictionary<string, PositionSnapshot>();
        var holdings = Securities[_symbol].Holdings;

        if (holdings.Quantity != 0m)
        {
            positions[AssetTicker] = new PositionSnapshot(
                AssetTicker,
                holdings.Quantity,
                holdings.AveragePrice,
                market.LastPrice);
        }

        return new PortfolioSnapshot(
            UtcTime,
            _protection.IsHalted ? TradingState.Halted : TradingState.PaperOnly,
            equity,
            _peakEquity,
            _dayStartEquity,
            Portfolio.Cash,
            positions);
    }
}
