namespace GE360.Trading.Domain;

/// <summary>
/// Strategy output only. This type is intentionally not an executable order.
/// Every signal must pass through sizing and risk before execution.
/// </summary>
public sealed record SignalIntent(
    Guid SignalId,
    string StrategyId,
    string Symbol,
    SignalDirection Direction,
    DateTime SignalTimeUtc,
    decimal Confidence,
    decimal ReferencePrice,
    decimal? SuggestedStopPrice,
    decimal? SuggestedTakeProfitPrice,
    string Reason)
{
    public static SignalIntent Create(
        string strategyId,
        string symbol,
        SignalDirection direction,
        DateTime signalTimeUtc,
        decimal confidence,
        decimal referencePrice,
        decimal? suggestedStopPrice,
        decimal? suggestedTakeProfitPrice,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(strategyId))
        {
            throw new ArgumentException("Strategy id is required.", nameof(strategyId));
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Symbol is required.", nameof(symbol));
        }

        if (confidence is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0 and 1.");
        }

        if (referencePrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(referencePrice), "Reference price must be positive.");
        }

        return new SignalIntent(
            Guid.NewGuid(),
            strategyId,
            symbol.Trim().ToUpperInvariant(),
            direction,
            signalTimeUtc.Kind == DateTimeKind.Utc ? signalTimeUtc : signalTimeUtc.ToUniversalTime(),
            confidence,
            referencePrice,
            suggestedStopPrice,
            suggestedTakeProfitPrice,
            reason ?? string.Empty);
    }
}
