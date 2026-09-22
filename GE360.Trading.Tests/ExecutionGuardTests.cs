using GE360.Trading.Domain;
using GE360.Trading.Execution;
using GE360.Trading.Protection;
using GE360.Trading.Risk;
using NUnit.Framework;

namespace GE360.Trading.Tests;

[TestFixture]
public sealed class ExecutionGuardTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 20, 0, 0, DateTimeKind.Utc);

    [Test]
    public void RejectsDuplicateApprovedSignal()
    {
        var guard = NewGuard();
        var order = ApproveEntry();
        var market = NewMarket();

        Assert.That(guard.Evaluate(order, market, Now).Allowed, Is.True);
        guard.RecordSubmission(order, Now);

        var second = guard.Evaluate(order, market, Now + TimeSpan.FromSeconds(1));

        Assert.That(second.Allowed, Is.False);
        Assert.That(second.Code, Is.EqualTo("DUPLICATE_SIGNAL"));
    }

    [Test]
    public void RejectsExpiredApproval()
    {
        var guard = NewGuard();
        var decision = guard.Evaluate(
            ApproveEntry(),
            NewMarket(),
            Now + TimeSpan.FromSeconds(6));

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.Code, Is.EqualTo("APPROVAL_EXPIRED"));
    }

    [Test]
    public void RejectsExcessiveSlippage()
    {
        var guard = NewGuard();
        var decision = guard.Evaluate(
            ApproveEntry(),
            NewMarket(last: 101m, bid: 100.95m, ask: 101.05m),
            Now);

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.Code, Is.EqualTo("SLIPPAGE_TOO_HIGH"));
    }

    [Test]
    public void RejectsExcessiveVolumeParticipation()
    {
        var guard = NewGuard();
        var decision = guard.Evaluate(
            ApproveEntry(),
            NewMarket(volume: 1_000m),
            Now);

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.Code, Is.EqualTo("VOLUME_PARTICIPATION_EXCEEDED"));
    }

    [Test]
    public void RateLimitBlocksAdditionalNewRisk()
    {
        var guard = NewGuard(maxPerMinute: 2);
        var first = ApproveEntry();
        var second = ApproveEntry();
        var third = ApproveEntry();
        var market = NewMarket();

        guard.RecordSubmission(first, Now);
        guard.RecordSubmission(second, Now + TimeSpan.FromSeconds(1));

        var decision = guard.Evaluate(third, market, Now + TimeSpan.FromSeconds(2));

        Assert.That(decision.Allowed, Is.False);
        Assert.That(decision.Code, Is.EqualTo("ORDER_RATE_LIMIT"));
    }

    [Test]
    public void RiskReducingExitBypassesLiquidityEntryChecks()
    {
        var guard = NewGuard();
        var exit = ApproveExit();

        var decision = guard.Evaluate(
            exit,
            NewMarket(last: 90m, bid: 89m, ask: 91m, volume: 0m),
            Now);

        Assert.That(exit.IsRiskReducing, Is.True);
        Assert.That(decision.Allowed, Is.True);
    }

    private static ExecutionGuard NewGuard(int maxPerMinute = 20)
        => new(new ExecutionGuardConfig(
            MaxApprovalAge: TimeSpan.FromSeconds(5),
            MaxSlippagePercent: 0.003m,
            MaxVolumeParticipationPercent: 0.01m,
            MaxSubmissionsPerMinute: maxPerMinute,
            DuplicateRetention: TimeSpan.FromHours(24)));

    private static ApprovedOrderIntent ApproveEntry()
    {
        var service = NewApprovalService();
        var signal = SignalIntent.Create(
            "exec-test",
            "AAPL",
            SignalDirection.Long,
            Now,
            0.8m,
            100m,
            98m,
            104m,
            "entry");

        return service.Evaluate(signal, NewMarket(), NewPortfolio()).Order
            ?? throw new InvalidOperationException("Expected approved order.");
    }

    private static ApprovedOrderIntent ApproveExit()
    {
        var service = NewApprovalService();
        var positions = new Dictionary<string, PositionSnapshot>
        {
            ["AAPL"] = new("AAPL", 25m, 98m, 100m)
        };

        var portfolio = NewPortfolio(positions);
        var signal = SignalIntent.Create(
            "exec-test",
            "AAPL",
            SignalDirection.Flat,
            Now,
            1m,
            100m,
            null,
            null,
            "exit");

        return service.Evaluate(signal, NewMarket(), portfolio).Order
            ?? throw new InvalidOperationException("Expected approved exit.");
    }

    private static TradeApprovalService NewApprovalService()
        => new(
            new ProtectionEngine(ProtectionConfig.ConservativePaperDefaults),
            new DefaultPreTradeRiskGate(RiskLimits.ConservativePaperDefaults));

    private static MarketSnapshot NewMarket(
        decimal last = 100m,
        decimal bid = 99.95m,
        decimal ask = 100.05m,
        decimal volume = 1_000_000m)
        => new("AAPL", Now, last, bid, ask, volume, 2m);

    private static PortfolioSnapshot NewPortfolio(
        IReadOnlyDictionary<string, PositionSnapshot>? positions = null)
        => new(
            Now,
            TradingState.PaperOnly,
            100_000m,
            100_000m,
            100_000m,
            100_000m,
            positions ?? new Dictionary<string, PositionSnapshot>());
}
