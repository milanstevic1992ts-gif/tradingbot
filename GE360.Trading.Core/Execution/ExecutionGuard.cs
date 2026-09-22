using GE360.Trading.Domain;

namespace GE360.Trading.Execution;

/// <summary>
/// Last deterministic gate immediately before a broker adapter.
/// Stateful checks make ApprovedOrderIntent effectively single-use.
/// </summary>
public sealed class ExecutionGuard
{
    private readonly ExecutionGuardConfig _config;
    private readonly Queue<DateTime> _submissionTimes = new();
    private readonly Dictionary<Guid, DateTime> _submittedSignals = new();

    public ExecutionGuard(ExecutionGuardConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        ValidateConfig(config);
    }

    public ExecutionGuardDecision Evaluate(
        ApprovedOrderIntent intent,
        MarketSnapshot market,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(market);

        Cleanup(utcNow);

        if (_submittedSignals.ContainsKey(intent.SignalId))
        {
            return ExecutionGuardDecision.Block(
                "DUPLICATE_SIGNAL",
                "This approved signal has already produced a submitted order.");
        }

        if (utcNow < intent.ApprovedAtUtc - TimeSpan.FromSeconds(1))
        {
            return ExecutionGuardDecision.Block(
                "EXECUTION_CLOCK_ERROR",
                "Execution time precedes approval time.");
        }

        if (utcNow - intent.ApprovedAtUtc > _config.MaxApprovalAge)
        {
            return ExecutionGuardDecision.Block(
                "APPROVAL_EXPIRED",
                "Risk approval is too old to execute.");
        }

        if (!string.Equals(intent.Symbol, market.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            return ExecutionGuardDecision.Block(
                "EXECUTION_SYMBOL_MISMATCH",
                "Approved order and market snapshot symbols differ.");
        }

        if (market.LastPrice <= 0m)
        {
            return ExecutionGuardDecision.Block(
                "EXECUTION_INVALID_PRICE",
                "Current execution price is invalid.");
        }

        // Risk-reducing exits are deliberately exempt from entry-side liquidity/rate checks.
        if (intent.IsRiskReducing)
        {
            return ExecutionGuardDecision.Allow();
        }

        var slippage = Math.Abs(market.LastPrice - intent.ReferencePrice) / intent.ReferencePrice;
        if (slippage > _config.MaxSlippagePercent)
        {
            return ExecutionGuardDecision.Block(
                "SLIPPAGE_TOO_HIGH",
                "Price moved too far after risk approval.");
        }

        if (market.Volume <= 0m)
        {
            return ExecutionGuardDecision.Block(
                "NO_VOLUME_DATA",
                "Volume data is unavailable for a new-risk order.");
        }

        var maximumQuantityFromVolume = market.Volume * _config.MaxVolumeParticipationPercent;
        if (Math.Abs(intent.Quantity) > maximumQuantityFromVolume)
        {
            return ExecutionGuardDecision.Block(
                "VOLUME_PARTICIPATION_EXCEEDED",
                "Order quantity exceeds configured volume participation.");
        }

        if (_submissionTimes.Count >= _config.MaxSubmissionsPerMinute)
        {
            return ExecutionGuardDecision.Block(
                "ORDER_RATE_LIMIT",
                "Maximum order submission rate reached.");
        }

        return ExecutionGuardDecision.Allow();
    }

    public void RecordSubmission(ApprovedOrderIntent intent, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(intent);
        Cleanup(utcNow);

        _submittedSignals[intent.SignalId] = utcNow;
        _submissionTimes.Enqueue(utcNow);
    }

    private void Cleanup(DateTime utcNow)
    {
        while (_submissionTimes.TryPeek(out var submittedAt) &&
               utcNow - submittedAt >= TimeSpan.FromMinutes(1))
        {
            _submissionTimes.Dequeue();
        }

        if (_submittedSignals.Count == 0)
        {
            return;
        }

        var cutoff = utcNow - _config.DuplicateRetention;
        foreach (var signalId in _submittedSignals
                     .Where(pair => pair.Value < cutoff)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _submittedSignals.Remove(signalId);
        }
    }

    private static void ValidateConfig(ExecutionGuardConfig config)
    {
        if (config.MaxApprovalAge <= TimeSpan.Zero ||
            config.MaxSlippagePercent < 0m ||
            config.MaxVolumeParticipationPercent is <= 0m or > 1m ||
            config.MaxSubmissionsPerMinute <= 0 ||
            config.DuplicateRetention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(config), "Execution guard configuration contains invalid values.");
        }
    }
}
