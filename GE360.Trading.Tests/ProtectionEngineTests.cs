using GE360.Trading.Domain;
using GE360.Trading.Protection;
using GE360.Trading.Risk;
using NUnit.Framework;

namespace GE360.Trading.Tests;

[TestFixture]
public sealed class ProtectionEngineTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 18, 0, 0, DateTimeKind.Utc);

    [Test]
    public void FlatSignalBypassesGlobalProtectionHalt()
    {
        var engine = NewEngine();
        engine.Halt("test halt");

        var decision = engine.Evaluate(NewSignal(SignalDirection.Flat), Now);

        Assert.That(decision.Allowed, Is.True);
    }

    [Test]
    public void SymbolCooldownBlocksImmediateReentry()
    {
        var engine = NewEngine();
        engine.RecordTradeOutcome(new TradeOutcome("s1", "AAPL", Now, 50m));

        var decision = engine.Evaluate(NewSignal(SignalDirection.Long), Now + TimeSpan.FromSeconds(30));

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.Code, Is.EqualTo("SYMBOL_COOLDOWN"));
    }

    [Test]
    public void LossStreakCoolsDownStrategy()
    {
        var engine = NewEngine();
        engine.RecordTradeOutcome(new TradeOutcome("s1", "MSFT", Now, -10m));
        engine.RecordTradeOutcome(new TradeOutcome("s1", "NVDA", Now + TimeSpan.FromMinutes(1), -10m));
        engine.RecordTradeOutcome(new TradeOutcome("s1", "AMD", Now + TimeSpan.FromMinutes(2), -10m));

        var decision = engine.Evaluate(
            NewSignal(SignalDirection.Long, symbol: "AAPL"),
            Now + TimeSpan.FromMinutes(3));

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.Code, Is.EqualTo("STRATEGY_COOLDOWN"));
    }

    [Test]
    public void RejectionStormHaltsProtectionEngine()
    {
        var engine = NewEngine(maxRejections: 3);

        engine.RecordRiskDecision(RiskDecision.Reject("R1", "one"));
        engine.RecordRiskDecision(RiskDecision.Reject("R2", "two"));
        engine.RecordRiskDecision(RiskDecision.Reject("R3", "three"));

        Assert.That(engine.IsHalted, Is.True);
        Assert.That(engine.Evaluate(NewSignal(SignalDirection.Long), Now).Allowed, Is.False);
    }

    [Test]
    public void RiskRequestedHaltIsImmediate()
    {
        var engine = NewEngine();

        engine.RecordRiskDecision(RiskDecision.Reject("MAX_DRAWDOWN", "breach", true));

        Assert.That(engine.IsHalted, Is.True);
        Assert.That(engine.HaltReason, Does.Contain("MAX_DRAWDOWN"));
    }

    [Test]
    public void ApprovedDecisionClearsRejectionStreak()
    {
        var engine = NewEngine(maxRejections: 3);
        engine.RecordRiskDecision(RiskDecision.Reject("R1", "one"));
        engine.RecordRiskDecision(RiskDecision.Approve(1m));

        Assert.That(engine.ConsecutiveRiskRejections, Is.Zero);
        Assert.That(engine.IsHalted, Is.False);
    }

    [Test]
    public void ManualResetRequiresReason()
    {
        var engine = NewEngine();
        engine.Halt("test");

        Assert.Throws<ArgumentException>(() => engine.ManualReset(string.Empty));

        engine.ManualReset("operator reviewed state");
        Assert.That(engine.IsHalted, Is.False);
    }

    private static ProtectionEngine NewEngine(int maxRejections = 10)
        => new(new ProtectionConfig(
            MaxConsecutiveLossesPerStrategy: 3,
            StrategyCooldownAfterLossStreak: TimeSpan.FromMinutes(15),
            SymbolCooldownAfterExit: TimeSpan.FromMinutes(2),
            MaxConsecutiveRiskRejections: maxRejections));

    private static SignalIntent NewSignal(
        SignalDirection direction,
        string symbol = "AAPL")
        => SignalIntent.Create(
            "s1",
            symbol,
            direction,
            Now,
            0.8m,
            100m,
            direction == SignalDirection.Long ? 98m :
                direction == SignalDirection.Short ? 102m : null,
            null,
            "test");
}
