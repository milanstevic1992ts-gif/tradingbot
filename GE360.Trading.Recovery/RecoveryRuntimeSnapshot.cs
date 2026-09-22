namespace GE360.Trading.Recovery;

public sealed record RecoveryRuntimeSnapshot(
    DateTime UtcTime,
    IReadOnlyDictionary<string, RecoveryPositionState> Positions,
    IReadOnlyList<RecoveryOpenOrderState> OpenOrders)
{
    public bool IsFlat =>
        Positions.Values.All(x => x.Quantity == 0m);

    public bool HasOpenOrders => OpenOrders.Count != 0;

    public static RecoveryRuntimeSnapshot Create(
        DateTime utcTime,
        IEnumerable<RecoveryPositionState> positions,
        IEnumerable<RecoveryOpenOrderState> openOrders)
    {
        var normalizedPositions = positions
            .Where(x => x.Quantity != 0m)
            .Select(x => x with
            {
                Symbol = RecoveryPositionState.NormalizeSymbol(x.Symbol)
            })
            .ToDictionary(
                x => x.Symbol,
                x => x,
                StringComparer.OrdinalIgnoreCase);

        var normalizedOrders = openOrders
            .Select(x => x.Normalize())
            .OrderBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LocalOrderId)
            .ToArray();

        return new RecoveryRuntimeSnapshot(
            EnsureUtc(utcTime),
            normalizedPositions,
            normalizedOrders);
    }

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();
}
