using GE360.Trading.Domain;
using GE360.Trading.Risk;
using NUnit.Framework;

namespace GE360.Trading.Tests;

[TestFixture]
public sealed class DefaultPreTradeRiskGateTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 17, 0, 0, DateTimeKind.Utc);

    [Test]
    public void ApprovesRiskSizedLongEntry()
    {
        var gate = new DefaultPreTradeRiskGate(RiskLimits.ConservativePaperDefaults);
        var signal = NewSignal(SignalDirection.Long, 100m, 98m);
        var market = NewMarket(100m, 99.95m, 100.05m);
        var portfolio = NewPortfolio();

        var decision = gate.Evaluate(signal, market, portfolio);

        Assert.That(decision.Approved, Is.True);
        Assert.That(decision.ApprovedQuantity, Is.GreaterThan(0m));
        Assert.That(decision.ApprovedQuantity * market.LastPrice, Is.LessThanOrEqualTo(10_000m));
    }

    [Test]
    public void RejectsStaleSignal()
    {
        var gate = new DefaultPreTradeRiskGate(RiskLimits.ConservativePaperDefaults);
        var signal = NewSignal(SignalDirection.Long, 100m, 98m) with
        {
            SignalTimeUtc = Now - TimeSpan.FromMinutes(5)
        };

        var decision = gate.Evaluate(signal, NewMarket(), NewPortfolio());

        Assert.That(decision.Approved, Is.False);
        Assert.That(decision.Code, Is.EqualTo("STALE_SIGNAL"));
    }

    [Test]
    public void RejectsWideSpread()
    {
        var gate = new DefaultPreTradeRiskGate(RiskLimits.ConservativePaperDefaults);
        var decision = gate.Evaluate(
            NewSignal(SignalDirection.Long, 100m, 98m),
            NewMarket(100m, 99m, 101m),
            NewPortfolio());

        Assert.That(decision.Approved, Is.False);
        Assert.That(decision.Code, Is.EqualTo("SPREAD_TOO_WIDE"));
    }

    [Test]
    public void DailyLossBreachRequestsGlobalHalt()
    {
        var gate = new DefaultPreTradeRiskGate(RiskLimits.ConservativePaperDefaults);
        var portfolio = NewPortfolio(equity: 97_000m, dayStartEquity: 100_000m);

        var decision = gate.Evaluate(
            NewSignal(SignalDirection.Long, 100m, 98m),
            NewMarket(),
            portfolio);

        Assert.That(decision.Approved, Is.False);
        Assert.That(decision.Code, Is.EqualTo("MAX_DAILY_LOSS"));
        Assert.That(decision.TriggerHalt, Is.True);
    }

    [Test]
    public void HaltedStateStillAllowsFlattening()
    {
        var gate = new DefaultPreTradeRiskGate(RiskLimits.ConservativePaperDefaults);
        var positions = new Dictionary<string, PositionSnapshot>
        {
            ["AAPL"] = new("AAPL", 25m, 98m, 100m)
        };

        var portfolio = NewPortfolio(
            tradingState: TradingState.Halted,
            positions: positions);

        var decision = gate.Evaluate(
            NewSignal(SignalDirection.Flat, 100m, null),
            NewMarket(),
            portfolio);

        Assert.That(decision.Approved, Is.True);
        Assert.That(decision.ApprovedQuantity, Is.EqualTo(-25m));
    }

    [Test]
    public void RejectsNewRiskWhileReducing()
    {
        var gate = new DefaultPreTradeRiskGate(RiskLimits.ConservativePaperDefaults);
        var decision = gate.Evaluate(
            NewSignal(SignalDirection.Long, 100m, 98m),
            NewMarket(),
            NewPortfolio(tradingState: TradingState.Reducing));

        Assert.That(decision.Approved, Is.False);
        Assert.That(decision.Code, Is.EqualTo("REDUCING_ONLY"));
    }

    [Test]
    public void RejectsShortWhenDisabled()
    {
        var gate = new DefaultPreTradeRiskGate(RiskLimits.ConservativePaperDefaults);
        var decision = gate.Evaluate(
            NewSignal(SignalDirection.Short, 100m, 102m),
            NewMarket(),
            NewPortfolio());

        Assert.That(decision.Approved, Is.False);
        Assert.That(decision.Code, Is.EqualTo("SHORT_DISABLED"));
    }

    [Test]
    public void RejectsInvalidStop()
    {
        var gate = new DefaultPreTradeRiskGate(RiskLimits.ConservativePaperDefaults);
        var decision = gate.Evaluate(
            NewSignal(SignalDirection.Long, 100m, 101m),
            NewMarket(),
            NewPortfolio());

        Assert.That(decision.Approved, Is.False);
        Assert.That(decision.Code, Is.EqualTo("INVALID_LONG_STOP"));
    }

    private static SignalIntent NewSignal(
        SignalDirection direction,
        decimal price = 100m,
        decimal? stop = 98m)
        => SignalIntent.Create(
            "test-strategy",
            "AAPL",
            direction,
            Now,
            0.8m,
            price,
            stop,
            null,
            "unit-test");

    private static MarketSnapshot NewMarket(
        decimal last = 100m,
        decimal bid = 99.95m,
        decimal ask = 100.05m)
        => new("AAPL", Now, last, bid, ask, 1_000_000m, 2m);

    private static PortfolioSnapshot NewPortfolio(
        TradingState tradingState = TradingState.PaperOnly,
        decimal equity = 100_000m,
        decimal peakEquity = 100_000m,
        decimal dayStartEquity = 100_000m,
        IReadOnlyDictionary<string, PositionSnapshot>? positions = null)
        => new(
            Now,
            tradingState,
            equity,
            peakEquity,
            dayStartEquity,
            100_000m,
            positions ?? new Dictionary<string, PositionSnapshot>());
}
