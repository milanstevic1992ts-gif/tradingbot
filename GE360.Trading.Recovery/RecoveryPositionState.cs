namespace GE360.Trading.Recovery;

public sealed record RecoveryPositionState(
    string Symbol,
    decimal Quantity,
    decimal AveragePrice,
    string? StrategyId,
    decimal? StopPrice,
    decimal? TakeProfitPrice)
{
    public bool HasExposure => Quantity != 0m;

    public bool HasProtectionMetadata =>
        !string.IsNullOrWhiteSpace(StrategyId) &&
        (StopPrice.HasValue || TakeProfitPrice.HasValue);

    public static RecoveryPositionState Runtime(
        string symbol,
        decimal quantity,
        decimal averagePrice)
        => new(
            NormalizeSymbol(symbol),
            quantity,
            averagePrice,
            null,
            null,
            null);

    public static string NormalizeSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException(
                "Recovery symbol is required.",
                nameof(symbol));
        }

        return symbol.Trim().ToUpperInvariant();
    }
}
