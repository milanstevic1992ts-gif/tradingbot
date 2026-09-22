namespace GE360.Trading.Recovery;

public sealed record RecoveryOpenOrderState(
    int LocalOrderId,
    string Symbol,
    decimal Quantity,
    string OrderType,
    string OrderStatus,
    string Tag,
    IReadOnlyList<string> BrokerageIds)
{
    public RecoveryOpenOrderState Normalize()
        => this with
        {
            Symbol = RecoveryPositionState.NormalizeSymbol(Symbol),
            OrderType = OrderType?.Trim() ?? string.Empty,
            OrderStatus = OrderStatus?.Trim() ?? string.Empty,
            Tag = Tag ?? string.Empty,
            BrokerageIds = BrokerageIds?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray()
                ?? Array.Empty<string>()
        };
}
