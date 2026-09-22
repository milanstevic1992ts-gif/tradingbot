namespace GE360.Trading.Recovery;

/// <summary>
/// Fail-closed startup reconciliation between the persisted GE360 expectation
/// and LEAN's broker-synchronized runtime state.
/// </summary>
public sealed class StartupReconciler
{
    private readonly RecoveryConfig _config;

    public StartupReconciler(RecoveryConfig? config = null)
    {
        _config = config ?? RecoveryConfig.ConservativeDefaults;
        _config.Validate();
    }

    public RecoveryReconciliationResult Evaluate(
        RecoveryCheckpoint? expected,
        RecoveryRuntimeSnapshot actual)
    {
        ArgumentNullException.ThrowIfNull(actual);

        var reasons = new List<string>();

        if (actual.OpenOrders.Count != 0)
        {
            reasons.Add("ACTUAL_OPEN_ORDERS_PRESENT");
        }

        if (expected is null)
        {
            if (actual.IsFlat && !actual.HasOpenOrders)
            {
                return RecoveryReconciliationResult.Synchronized(
                    reasons: new[] { "FRESH_FLAT_START" });
            }

            reasons.Add("NO_CHECKPOINT_WITH_RUNTIME_EXPOSURE");
            return RecoveryReconciliationResult.Reduce(actual, reasons);
        }

        if (expected.OpenOrders.Count != 0)
        {
            reasons.Add("CHECKPOINT_CONTAINS_OPEN_ORDERS");
        }

        var positionMismatches = ComparePositions(
            expected.Positions,
            actual.Positions);

        reasons.AddRange(positionMismatches);

        if (actual.OpenOrders.Count != 0)
        {
            return RecoveryReconciliationResult.Reduce(actual, reasons);
        }

        if (positionMismatches.Count != 0 ||
            expected.OpenOrders.Count != 0)
        {
            if (actual.IsFlat)
            {
                reasons.Add("RUNTIME_FLAT_BASELINE_CAN_BE_RESET");
                return RecoveryReconciliationResult.BaselineReset(
                    reasons.ToArray());
            }

            return RecoveryReconciliationResult.Reduce(actual, reasons);
        }

        if (!actual.IsFlat)
        {
            if (actual.UtcTime - expected.UpdatedAtUtc >
                _config.MaxCheckpointAgeWithExposure)
            {
                reasons.Add("EXPOSED_CHECKPOINT_STALE");
                return RecoveryReconciliationResult.Reduce(
                    actual,
                    reasons);
            }

            var missingProtection = expected.Positions.Values
                .Where(x => x.Quantity != 0m)
                .Where(x => !x.HasProtectionMetadata)
                .Select(x => x.Symbol)
                .ToArray();

            if (missingProtection.Length != 0)
            {
                reasons.Add(
                    $"PROTECTION_METADATA_MISSING:{string.Join(",", missingProtection)}");

                return RecoveryReconciliationResult.Reduce(
                    actual,
                    reasons);
            }
        }

        return RecoveryReconciliationResult.Synchronized(
            expected.Positions.Values
                .Where(x => x.Quantity != 0m),
            "CHECKPOINT_MATCH");
    }

    private List<string> ComparePositions(
        IReadOnlyDictionary<string, RecoveryPositionState> expected,
        IReadOnlyDictionary<string, RecoveryPositionState> actual)
    {
        var mismatches = new List<string>();

        foreach (var symbol in expected.Keys
                     .Concat(actual.Keys)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var expectedQuantity = expected.TryGetValue(symbol, out var e)
                ? e.Quantity
                : 0m;

            var actualQuantity = actual.TryGetValue(symbol, out var a)
                ? a.Quantity
                : 0m;

            if (Math.Abs(expectedQuantity - actualQuantity) >
                _config.QuantityTolerance)
            {
                mismatches.Add(
                    $"POSITION_MISMATCH:{symbol}:expected={expectedQuantity}:actual={actualQuantity}");
            }
        }

        return mismatches;
    }
}
