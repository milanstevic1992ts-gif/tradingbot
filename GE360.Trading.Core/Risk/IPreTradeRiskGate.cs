using GE360.Trading.Domain;

namespace GE360.Trading.Risk;

public interface IPreTradeRiskGate
{
    RiskDecision Evaluate(
        SignalIntent signal,
        MarketSnapshot market,
        PortfolioSnapshot portfolio);
}
