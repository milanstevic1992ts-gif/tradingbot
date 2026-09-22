namespace GE360.Trading.Recovery;

public sealed record RecoveryCheckpoint(
    DateTime UpdatedAtUtc,
    bool GracefulShutdown,
    IReadOnlyDictionary<string, RecoveryPositionState> Positions,
    IReadOnlyList<RecoveryOpenOrderState> OpenOrders)
{
    public bool HasExposure =>
        Positions.Values.Any(x => x.Quantity != 0m);

    public static RecoveryCheckpoint Create(
        DateTime updatedAtUtc,
        bool gracefulShutdown,
        IEnumerable<RecoveryPositionState> positions,
        IEnumerable<RecoveryOpenOrderState> openOrders)
    {
        var positionMap = positions
            .Where(x => x.Quantity != 0m)
            .Select(x => x with
            {
                Symbol = RecoveryPositionState.NormalizeSymbol(x.Symbol)
            })
            .ToDictionary(
                x => x.Symbol,
                x => x,
                StringComparer.OrdinalIgnoreCase);

        return new RecoveryCheckpoint(
            updatedAtUtc.Kind == DateTimeKind.Utc
                ? updatedAtUtc
                : updatedAtUtc.ToUniversalTime(),
            gracefulShutdown,
            positionMap,
            openOrders
                .Select(x => x.Normalize())
                .OrderBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.LocalOrderId)
                .ToArray());
    }
}
