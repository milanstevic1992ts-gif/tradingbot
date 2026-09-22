using GE360.Trading.Domain;
using GE360.Trading.Execution;
using GE360.Trading.Protection;
using GE360.Trading.Risk;
using NUnit.Framework;

namespace GE360.Trading.Tests;

[TestFixture]
public sealed class PositionProtectionMonitorTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 20, 30, 0, DateTimeKind.Utc);

    [Test]
    public void EmitsFlatSignalWhenLongStopIsHit()
    {
        var monitor = new PositionProtectionMonitor();
        monitor.RegisterEntry(ApproveLongEntry());

        var signal = monitor.Evaluate(
            Market(97.5m),
            PortfolioWithLong(),
            Now + TimeSpan.FromMinutes(1));

        Assert.That(signal, Is.Not.Null);
        Assert.That(signal!.Direction, Is.EqualTo(SignalDirection.Flat));
        Assert.That(signal.Reason, Is.EqualTo("protective-stop-triggered"));
    }

    [Test]
    public void EmitsFlatSignalWhenTakeProfitIsHit()
    {
        var monitor = new PositionProtectionMonitor();
        monitor.RegisterEntry(ApproveLongEntry());

        var signal = monitor.Evaluate(
            Market(104.5m),
            PortfolioWithLong(),
            Now + TimeSpan.FromMinutes(1));

        Assert.That(signal, Is.Not.Null);
        Assert.That(signal!.Reason, Is.EqualTo("take-profit-triggered"));
    }

    [Test]
    public void DoesNotEmitDuplicateExitWhilePending()
    {
        var monitor = new PositionProtectionMonitor();
        monitor.RegisterEntry(ApproveLongEntry());

        var first = monitor.Evaluate(Market(97.5m), PortfolioWithLong(), Now);
        var second = monitor.Evaluate(Market(97m), PortfolioWithLong(), Now + TimeSpan.FromSeconds(1));

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Null);
    }

    [Test]
    public void FailedExitSubmissionAllowsRetry()
    {
        var monitor = new PositionProtectionMonitor();
        monitor.RegisterEntry(ApproveLongEntry());

        var first = monitor.Evaluate(Market(97.5m), PortfolioWithLong(), Now);
        monitor.MarkExitSubmissionFailed("AAPL");
        var retry = monitor.Evaluate(Market(97m), PortfolioWithLong(), Now + TimeSpan.FromSeconds(1));

        Assert.That(first, Is.Not.Null);
        Assert.That(retry, Is.Not.Null);
    }

    [Test]
    public void ClearsProtectionWhenPositionIsFlat()
    {
        var monitor = new PositionProtectionMonitor();
        monitor.RegisterEntry(ApproveLongEntry());

        var flatPortfolio = new PortfolioSnapshot(
            Now,
            TradingState.PaperOnly,
            100_000m,
            100_000m,
            100_000m,
            100_000m,
            new Dictionary<string, PositionSnapshot>());

        var signal = monitor.Evaluate(Market(97m), flatPortfolio, Now);

        Assert.That(signal, Is.Null);
    }

    private static ApprovedOrderIntent ApproveLongEntry()
    {
        var service = new TradeApprovalService(
            new ProtectionEngine(ProtectionConfig.ConservativePaperDefaults),
            new DefaultPreTradeRiskGate(RiskLimits.ConservativePaperDefaults));

        var signal = SignalIntent.Create(
            "protection-test",
            "AAPL",
            SignalDirection.Long,
            Now,
            0.8m,
            100m,
            98m,
            104m,
            "entry");

        return service.Evaluate(
                signal,
                Market(100m),
                new PortfolioSnapshot(
                    Now,
                    TradingState.PaperOnly,
                    100_000m,
                    100_000m,
                    100_000m,
                    100_000m,
                    new Dictionary<string, PositionSnapshot>()))
            .Order ?? throw new InvalidOperationException("Expected approval.");
    }

    private static MarketSnapshot Market(decimal price)
        => new("AAPL", Now, price, price - 0.02m, price + 0.02m, 1_000_000m, 2m);

    private static PortfolioSnapshot PortfolioWithLong()
        => new(
            Now,
            TradingState.PaperOnly,
            100_000m,
            100_000m,
            100_000m,
            95_000m,
            new Dictionary<string, PositionSnapshot>
            {
                ["AAPL"] = new("AAPL", 25m, 100m, 100m)
            });
}
