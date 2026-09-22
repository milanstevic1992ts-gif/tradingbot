using System.Text.Json;
using GE360.Trading.Domain;
using GE360.Trading.Execution;
using GE360.Trading.Protection;
using GE360.Trading.Recovery;
using QuantConnect.Algorithm;

namespace GE360.Trading.LeanAlgorithm;

/// <summary>
/// Bridges broker-neutral GE360 recovery logic to LEAN runtime state.
/// Startup is fail-closed: new risk remains blocked until reconciliation completes.
/// </summary>
public sealed class LeanRecoveryCoordinator
{
    public const string CheckpointPathEnvironmentVariable =
        "GE360_RECOVERY_CHECKPOINT";

    private readonly RecoveryCheckpointStore _store;
    private readonly StartupReconciler _reconciler;
    private readonly Dictionary<string, RecoveryPositionState> _protectionMetadata =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string>? _log;

    private RecoveryCheckpoint? _expected;
    private string? _loadError;
    private bool _startupComplete;
    private bool _persistenceHealthy = true;

    public LeanRecoveryCoordinator(
        string? checkpointPath = null,
        RecoveryConfig? config = null,
        Action<string>? log = null)
    {
        var resolvedPath = string.IsNullOrWhiteSpace(checkpointPath)
            ? Path.Combine("ge360-state", "recovery-checkpoint.json")
            : checkpointPath;

        _store = new RecoveryCheckpointStore(resolvedPath);
        _reconciler = new StartupReconciler(config);
        _log = log;

        try
        {
            _expected = _store.Load();

            if (_expected is not null)
            {
                foreach (var position in _expected.Positions.Values)
                {
                    if (position.HasProtectionMetadata)
                    {
                        _protectionMetadata[position.Symbol] = position;
                    }
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            JsonException)
        {
            _loadError =
                $"{exception.GetType().Name}: {exception.Message}";
            _persistenceHealthy = false;
        }
    }

    public string CheckpointPath => _store.Path;

    public bool StartupComplete => _startupComplete;

    public bool PersistenceHealthy => _persistenceHealthy;

    public string? LastError { get; private set; }

    public RecoveryReconciliationResult? LastResult { get; private set; }

    public TradingState TradingState =>
        _startupComplete && _persistenceHealthy
            ? TradingState.PaperOnly
            : TradingState.Reducing;

    public RecoveryReconciliationResult ReconcileStartup(
        QCAlgorithm algorithm,
        PositionProtectionMonitor protectionMonitor)
    {
        ArgumentNullException.ThrowIfNull(algorithm);
        ArgumentNullException.ThrowIfNull(protectionMonitor);

        var actual = Capture(algorithm);

        if (_startupComplete)
        {
            var ready = RecoveryReconciliationResult.Synchronized(
                reasons: new[] { "STARTUP_ALREADY_RECONCILED" });
            LastResult = ready;
            return ready;
        }

        if (_loadError is not null)
        {
            if (!actual.IsFlat || actual.HasOpenOrders)
            {
                var blocked = RecoveryReconciliationResult.Reduce(
                    actual,
                    new[]
                    {
                        "CHECKPOINT_LOAD_FAILED",
                        _loadError
                    });

                LastResult = blocked;
                return blocked;
            }

            if (!TryPersistSnapshot(
                    actual,
                    gracefulShutdown: false))
            {
                var blocked = RecoveryReconciliationResult.Reduce(
                    actual,
                    new[]
                    {
                        "CHECKPOINT_RESET_WRITE_FAILED",
                        LastError ?? "unknown"
                    });

                LastResult = blocked;
                return blocked;
            }

            _loadError = null;
            _startupComplete = true;

            var reset = RecoveryReconciliationResult.BaselineReset(
                "CORRUPT_CHECKPOINT_RESET_WHILE_FLAT");

            LastResult = reset;
            return reset;
        }

        var result = _reconciler.Evaluate(
            _expected,
            actual);

        LastResult = result;

        if (result.Mode == RecoveryMode.Synchronized)
        {
            foreach (var position in result.ProtectionsToRestore)
            {
                protectionMonitor.Restore(
                    position.StrategyId!,
                    position.Symbol,
                    position.Quantity,
                    position.StopPrice,
                    position.TakeProfitPrice);

                _protectionMetadata[position.Symbol] = position;
            }

            _startupComplete = true;

            if (!TryPersistSnapshot(
                    actual,
                    gracefulShutdown: false))
            {
                _startupComplete = false;

                var blocked = RecoveryReconciliationResult.Reduce(
                    actual,
                    new[]
                    {
                        "CHECKPOINT_WRITE_FAILED_AFTER_RECONCILIATION",
                        LastError ?? "unknown"
                    });

                LastResult = blocked;
                return blocked;
            }

            _log?.Invoke(
                $"GE360 recovery synchronized: {string.Join(",", result.Reasons)}");

            return result;
        }

        if (result.Mode == RecoveryMode.BaselineResetRequired)
        {
            _protectionMetadata.Clear();

            if (!TryPersistSnapshot(
                    actual,
                    gracefulShutdown: false))
            {
                var blocked = RecoveryReconciliationResult.Reduce(
                    actual,
                    new[]
                    {
                        "BASELINE_RESET_WRITE_FAILED",
                        LastError ?? "unknown"
                    });

                LastResult = blocked;
                return blocked;
            }

            _startupComplete = true;
            _log?.Invoke(
                $"GE360 recovery baseline reset: {string.Join(",", result.Reasons)}");
        }

        return result;
    }

    public RecoveryRuntimeSnapshot Capture(
        QCAlgorithm algorithm)
    {
        ArgumentNullException.ThrowIfNull(algorithm);

        var positions = algorithm.Securities.Values
            .Where(security =>
                security.Holdings.Quantity != 0m)
            .Select(security =>
                RecoveryPositionState.Runtime(
                    security.Symbol.Value,
                    security.Holdings.Quantity,
                    security.Holdings.AveragePrice))
            .ToArray();

        var openOrders = algorithm.Transactions
            .GetOpenOrders()
            .Select(order => new RecoveryOpenOrderState(
                order.Id,
                order.Symbol.Value,
                order.Quantity,
                order.Type.ToString(),
                order.Status.ToString(),
                order.Tag ?? string.Empty,
                order.BrokerId?.ToArray()
                    ?? Array.Empty<string>()))
            .ToArray();

        return RecoveryRuntimeSnapshot.Create(
            algorithm.UtcTime,
            positions,
            openOrders);
    }

    public void RegisterEntry(
        ApprovedOrderIntent order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.IsRiskReducing ||
            order.Quantity == 0m)
        {
            return;
        }

        _protectionMetadata[order.Symbol] =
            new RecoveryPositionState(
                order.Symbol,
                order.Quantity,
                order.ReferencePrice,
                order.StrategyId,
                order.StopPrice,
                order.TakeProfitPrice);
    }

    public void ClearProtection(string symbol)
    {
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            _protectionMetadata.Remove(
                symbol.Trim().ToUpperInvariant());
        }
    }

    public bool PersistRuntime(
        QCAlgorithm algorithm,
        bool gracefulShutdown)
    {
        ArgumentNullException.ThrowIfNull(algorithm);

        return TryPersistSnapshot(
            Capture(algorithm),
            gracefulShutdown);
    }

    private bool TryPersistSnapshot(
        RecoveryRuntimeSnapshot actual,
        bool gracefulShutdown)
    {
        try
        {
            foreach (var symbol in _protectionMetadata.Keys
                         .Where(symbol =>
                             !actual.Positions.ContainsKey(symbol))
                         .ToArray())
            {
                _protectionMetadata.Remove(symbol);
            }

            var positions = actual.Positions.Values
                .Select(position =>
                {
                    if (_protectionMetadata.TryGetValue(
                            position.Symbol,
                            out var metadata))
                    {
                        return position with
                        {
                            StrategyId = metadata.StrategyId,
                            StopPrice = metadata.StopPrice,
                            TakeProfitPrice = metadata.TakeProfitPrice
                        };
                    }

                    return position;
                })
                .ToArray();

            var checkpoint = RecoveryCheckpoint.Create(
                actual.UtcTime,
                gracefulShutdown,
                positions,
                actual.OpenOrders);

            _store.Save(checkpoint);
            _expected = checkpoint;
            _persistenceHealthy = true;
            LastError = null;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            JsonException)
        {
            _persistenceHealthy = false;
            LastError =
                $"{exception.GetType().Name}: {exception.Message}";

            _log?.Invoke(
                $"GE360 recovery checkpoint persistence failed: {exception.GetType().Name}.");

            return false;
        }
    }
}
