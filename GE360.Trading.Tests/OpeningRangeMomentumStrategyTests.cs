using GE360.Trading.Domain;
using GE360.Trading.Strategies;
using GE360.Trading.Risk;
using NUnit.Framework;

namespace GE360.Trading.Tests;

[TestFixture]
public sealed class OpeningRangeMomentumStrategyTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 14, 0, 0, DateTimeKind.Utc);

    [Test]
    public void EmitsLongSignalForConfirmedOpeningRangeBreakout()
    {
        var strategy = new OpeningRangeMomentumStrategy();

        var signals = strategy.Evaluate(NewContext(
            price: 102m,
            vwap: 100m,
            openingRangeHigh: 101m,
            relativeVolume: 2.0m)).ToList();

        Assert.That(signals, Has.Count.EqualTo(1));
        Assert.That(signals[0].Direction, Is.EqualTo(SignalDirection.Long));
        Assert.That(signals[0].SuggestedStopPrice, Is.LessThan(signals[0].ReferencePrice));
        Assert.That(signals[0].SuggestedTakeProfitPrice, Is.GreaterThan(signals[0].ReferencePrice));
    }

    [Test]
    public void DoesNotSignalBelowVwap()
    {
        var strategy = new OpeningRangeMomentumStrategy();

        var signals = strategy.Evaluate(NewContext(
            price: 102m,
            vwap: 103m,
            openingRangeHigh: 101m,
            relativeVolume: 2m)).ToList();

        Assert.That(signals, Is.Empty);
    }

    [Test]
    public void DoesNotSignalWhenRelativeVolumeIsLow()
    {
        var strategy = new OpeningRangeMomentumStrategy();

        var signals = strategy.Evaluate(NewContext(
            price: 102m,
            vwap: 100m,
            openingRangeHigh: 101m,
            relativeVolume: 1.1m)).ToList();

        Assert.That(signals, Is.Empty);
    }

    [Test]
    public void DoesNotSignalBeforeOpeningRangeCompletes()
    {
        var strategy = new OpeningRangeMomentumStrategy();

        var signals = strategy.Evaluate(NewContext(
            price: 102m,
            vwap: 100m,
            openingRangeHigh: 101m,
            relativeVolume: 2m,
            openingRangeComplete: false)).ToList();

        Assert.That(signals, Is.Empty);
    }

    [Test]
    public void EnforcesMinimumSignalIntervalPerSymbol()
    {
        var strategy = new OpeningRangeMomentumStrategy();
        var first = strategy.Evaluate(NewContext(
            price: 102m,
            vwap: 100m,
            openingRangeHigh: 101m,
            relativeVolume: 2m)).ToList();

        var secondContext = NewContext(
            price: 103m,
            vwap: 100m,
            openingRangeHigh: 101m,
            relativeVolume: 2m,
            utcTime: Now + TimeSpan.FromMinutes(1));

        var second = strategy.Evaluate(secondContext).ToList();

        Assert.That(first, Has.Count.EqualTo(1));
        Assert.That(second, Is.Empty);
    }

    [Test]
    public void ConfidenceRemainsBounded()
    {
        var strategy = new OpeningRangeMomentumStrategy();

        var signal = strategy.Evaluate(NewContext(
            price: 110m,
            vwap: 100m,
            openingRangeHigh: 101m,
            relativeVolume: 10m)).Single();

        Assert.That(signal.Confidence, Is.InRange(0m, 0.95m));
    }


    [Test]
    public void DoesNotAddRiskWhenPositionAlreadyOpen()
    {
        var strategy = new OpeningRangeMomentumStrategy();
        var context = NewContext(
            price: 102m,
            vwap: 100m,
            openingRangeHigh: 101m,
            relativeVolume: 2m);

        context = context with
        {
            Portfolio = new PortfolioSnapshot(
                Now,
                TradingState.PaperOnly,
                100_000m,
                100_000m,
                100_000m,
                90_000m,
                new Dictionary<string, PositionSnapshot>
                {
                    ["AAPL"] = new("AAPL", 10m, 100m, 102m)
                })
        };

        var signals = strategy.Evaluate(context).ToList();

        Assert.That(signals, Is.Empty);
    }

    private static StrategyContext NewContext(
        decimal price,
        decimal vwap,
        decimal openingRangeHigh,
        decimal relativeVolume,
        bool openingRangeComplete = true,
        DateTime? utcTime = null)
    {
        var time = utcTime ?? Now;
        var market = new MarketSnapshot(
            "AAPL",
            time,
            price,
            price - 0.02m,
            price + 0.02m,
            2_000_000m,
            1.5m);

        var features = new StrategyFeatures(
            "AAPL",
            vwap,
            openingRangeHigh,
            openingRangeHigh - 2m,
            relativeVolume,
            openingRangeComplete);

        return new StrategyContext(
            time,
            new Dictionary<string, MarketSnapshot> { ["AAPL"] = market },
            new Dictionary<string, StrategyFeatures> { ["AAPL"] = features });
    }
}
