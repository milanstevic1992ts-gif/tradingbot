using GE360.Trading.Domain;
using GE360.Trading.Execution;
using GE360.Trading.Protection;
using GE360.Trading.Risk;
using NUnit.Framework;

namespace GE360.Trading.Tests;

[TestFixture]
public sealed class TradeApprovalServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 19, 0, 0, DateTimeKind.Utc);

    [Test]
    public void ProducesApprovedOrderOnlyAfterProtectionAndRiskPass()
    {
        var service = NewService();
        var result = service.Evaluate(
            NewSignal(),
            NewMarket(),
            NewPortfolio());

        Assert.That(result.Approved, Is.True);
        Assert.That(result.Order, Is.Not.Null);
        Assert.That(result.Order!.Quantity, Is.GreaterThan(0m));
        Assert.That(result.Order.SignalId, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public void ProtectionBlockPreventsOrderCreation()
    {
        var protection = new ProtectionEngine(ProtectionConfig.ConservativePaperDefaults);
        protection.Halt("operator halt");

        var service = new TradeApprovalService(
            protection,
            new DefaultPreTradeRiskGate(RiskLimits.ConservativePaperDefaults));

        var result = service.Evaluate(NewSignal(), NewMarket(), NewPortfolio());

        Assert.That(result.Approved, Is.False);
        Assert.That(result.Order, Is.Null);
        Assert.That(result.Code, Is.EqualTo("PROTECTION_HALTED"));
    }

    [Test]
    public void RiskRejectionPreventsOrderCreation()
    {
        var service = NewService();
        var stale = NewSignal() with
        {
            SignalTimeUtc = Now - TimeSpan.FromMinutes(10)
        };

        var result = service.Evaluate(stale, NewMarket(), NewPortfolio());

        Assert.That(result.Approved, Is.False);
        Assert.That(result.Order, Is.Null);
        Assert.That(result.Code, Is.EqualTo("STALE_SIGNAL"));
    }

    private static TradeApprovalService NewService()
        => new(
            new ProtectionEngine(ProtectionConfig.ConservativePaperDefaults),
            new DefaultPreTradeRiskGate(RiskLimits.ConservativePaperDefaults));

    private static SignalIntent NewSignal()
        => SignalIntent.Create(
            "approval-test",
            "AAPL",
            SignalDirection.Long,
            Now,
            0.8m,
            100m,
            98m,
            104m,
            "test");

    private static MarketSnapshot NewMarket()
        => new("AAPL", Now, 100m, 99.95m, 100.05m, 1_000_000m, 2m);

    private static PortfolioSnapshot NewPortfolio()
        => new(
            Now,
            TradingState.PaperOnly,
            100_000m,
            100_000m,
            100_000m,
            100_000m,
            new Dictionary<string, PositionSnapshot>());
}
