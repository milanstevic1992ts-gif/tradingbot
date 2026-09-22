using GE360.Trading.Domain;

namespace GE360.Trading.Recovery;

public enum RecoveryMode
{
    Synchronized = 0,
    BaselineResetRequired = 1,
    Reducing = 2
}

public sealed record RecoveryReconciliationResult(
    RecoveryMode Mode,
    TradingState TradingState,
    bool AllowNewEntries,
    bool RequiresCancelOpenOrders,
    IReadOnlyList<string> SymbolsToFlatten,
    IReadOnlyList<RecoveryPositionState> ProtectionsToRestore,
    IReadOnlyList<string> Reasons)
{
    public bool IsSynchronized =>
        Mode == RecoveryMode.Synchronized;

    public static RecoveryReconciliationResult Synchronized(
        IEnumerable<RecoveryPositionState>? protections = null,
        params string[] reasons)
        => new(
            RecoveryMode.Synchronized,
            TradingState.PaperOnly,
            true,
            false,
            Array.Empty<string>(),
            protections?.ToArray()
                ?? Array.Empty<RecoveryPositionState>(),
            reasons);

    public static RecoveryReconciliationResult BaselineReset(
        params string[] reasons)
        => new(
            RecoveryMode.BaselineResetRequired,
            TradingState.Reducing,
            false,
            false,
            Array.Empty<string>(),
            Array.Empty<RecoveryPositionState>(),
            reasons);

    public static RecoveryReconciliationResult Reduce(
        RecoveryRuntimeSnapshot actual,
        IEnumerable<string> reasons)
        => new(
            RecoveryMode.Reducing,
            TradingState.Reducing,
            false,
            actual.OpenOrders.Count != 0,
            actual.Positions.Values
                .Where(x => x.Quantity != 0m)
                .Select(x => x.Symbol)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Array.Empty<RecoveryPositionState>(),
            reasons.ToArray());
}
