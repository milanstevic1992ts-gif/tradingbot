using GE360.Trading.Domain;
using GE360.Trading.Risk;

namespace GE360.Trading.Protection;

/// <summary>
/// Stateful protection layer for recent trading behavior.
/// It protects new risk, never blocks a Flat/risk-reducing signal.
/// </summary>
public sealed class ProtectionEngine
{
    private readonly ProtectionConfig _config;
    private readonly Dictionary<string, DateTime> _symbolCooldownUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _strategyCooldownUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _consecutiveLosses = new(StringComparer.OrdinalIgnoreCase);

    private int _consecutiveRiskRejections;
    private bool _halted;
    private string _haltReason = string.Empty;

    public ProtectionEngine(ProtectionConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        ValidateConfig(config);
    }

    public bool IsHalted => _halted;
    public string HaltReason => _haltReason;
    public int ConsecutiveRiskRejections => _consecutiveRiskRejections;

    public ProtectionDecision Evaluate(SignalIntent signal, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(signal);

        // A protection layer must never trap the system in a risky position.
        if (signal.Direction == SignalDirection.Flat)
        {
            return ProtectionDecision.Allow();
        }

        if (_halted)
        {
            return ProtectionDecision.Block("PROTECTION_HALTED", _haltReason);
        }

        if (_symbolCooldownUntil.TryGetValue(signal.Symbol, out var symbolUntil) && utcNow < symbolUntil)
        {
            return ProtectionDecision.Block(
                "SYMBOL_COOLDOWN",
                $"Symbol is cooling down until {symbolUntil:O}.");
        }

        if (_strategyCooldownUntil.TryGetValue(signal.StrategyId, out var strategyUntil) && utcNow < strategyUntil)
        {
            return ProtectionDecision.Block(
                "STRATEGY_COOLDOWN",
                $"Strategy is cooling down until {strategyUntil:O}.");
        }

        return ProtectionDecision.Allow();
    }

    public void RecordTradeOutcome(TradeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        _symbolCooldownUntil[outcome.Symbol] = outcome.ClosedAtUtc + _config.SymbolCooldownAfterExit;

        if (outcome.RealizedPnl < 0m)
        {
            var streak = _consecutiveLosses.TryGetValue(outcome.StrategyId, out var current)
                ? current + 1
                : 1;

            if (streak >= _config.MaxConsecutiveLossesPerStrategy)
            {
                _strategyCooldownUntil[outcome.StrategyId] =
                    outcome.ClosedAtUtc + _config.StrategyCooldownAfterLossStreak;
                _consecutiveLosses[outcome.StrategyId] = 0;
            }
            else
            {
                _consecutiveLosses[outcome.StrategyId] = streak;
            }
        }
        else
        {
            _consecutiveLosses[outcome.StrategyId] = 0;
        }
    }

    public void RecordRiskDecision(RiskDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.Approved)
        {
            _consecutiveRiskRejections = 0;
            return;
        }

        if (decision.TriggerHalt)
        {
            Halt($"Risk gate requested halt: {decision.Code}.");
            return;
        }

        // Expected trading rejections (spread, stop geometry, no capacity, etc.)
        // are normal controls and must never accumulate into a system halt.
        if (!decision.CountsTowardRejectionStorm)
        {
            return;
        }

        _consecutiveRiskRejections++;

        if (_consecutiveRiskRejections >= _config.MaxConsecutiveRiskRejections)
        {
            Halt($"Structural risk rejection storm: {_consecutiveRiskRejections} consecutive failures.");
        }
    }

    public void Halt(string reason)
    {
        _halted = true;
        _haltReason = string.IsNullOrWhiteSpace(reason)
            ? "Protection engine halted trading."
            : reason;
    }

    /// <summary>
    /// Explicit operator boundary. Strategies are never given a ProtectionEngine reference.
    /// </summary>
    public void ManualReset(string operatorReason)
    {
        if (string.IsNullOrWhiteSpace(operatorReason))
        {
            throw new ArgumentException("Manual reset requires an operator reason.", nameof(operatorReason));
        }

        _halted = false;
        _haltReason = string.Empty;
        _consecutiveRiskRejections = 0;
    }

    private static void ValidateConfig(ProtectionConfig config)
    {
        if (config.MaxConsecutiveLossesPerStrategy <= 0 ||
            config.StrategyCooldownAfterLossStreak < TimeSpan.Zero ||
            config.SymbolCooldownAfterExit < TimeSpan.Zero ||
            config.MaxConsecutiveRiskRejections <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(config), "Protection configuration contains invalid values.");
        }
    }
}
