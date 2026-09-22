using GE360.Trading.Domain;
using GE360.Trading.Protection;
using GE360.Trading.Risk;

namespace GE360.Trading.Execution;

/// <summary>
/// Single approval pipeline. Protection is checked first, then deterministic risk.
/// Only this service can manufacture ApprovedOrderIntent.
/// </summary>
public sealed class TradeApprovalService
{
    private readonly ProtectionEngine _protection;
    private readonly IPreTradeRiskGate _riskGate;

    public TradeApprovalService(
        ProtectionEngine protection,
        IPreTradeRiskGate riskGate)
    {
        _protection = protection ?? throw new ArgumentNullException(nameof(protection));
        _riskGate = riskGate ?? throw new ArgumentNullException(nameof(riskGate));
    }

    public TradeApprovalResult Evaluate(
        SignalIntent signal,
        MarketSnapshot market,
        PortfolioSnapshot portfolio)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(portfolio);

        var protectionDecision = _protection.Evaluate(signal, portfolio.UtcTime);
        if (!protectionDecision.Allowed)
        {
            return TradeApprovalResult.Reject(
                protectionDecision.Code,
                protectionDecision.Reason);
        }

        var riskDecision = _riskGate.Evaluate(signal, market, portfolio);
        _protection.RecordRiskDecision(riskDecision);

        if (!riskDecision.Approved)
        {
            return TradeApprovalResult.Reject(
                riskDecision.Code,
                riskDecision.Reason);
        }

        var order = new ApprovedOrderIntent(
            signal.SignalId,
            signal.StrategyId,
            signal.Symbol,
            riskDecision.ApprovedQuantity,
            portfolio.UtcTime,
            market.LastPrice,
            signal.SuggestedStopPrice,
            signal.SuggestedTakeProfitPrice,
            signal.Reason,
            signal.Direction == SignalDirection.Flat);

        return TradeApprovalResult.Approve(order);
    }
}
