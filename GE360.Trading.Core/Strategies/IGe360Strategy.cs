using GE360.Trading.Domain;

namespace GE360.Trading.Strategies;

/// <summary>
/// GE360 strategies can emit SignalIntent objects only.
/// They deliberately have no broker or order-submission API.
/// </summary>
public interface IGe360Strategy
{
    string StrategyId { get; }

    IEnumerable<SignalIntent> Evaluate(StrategyContext context);
}
