using GE360.Trading.Domain;

namespace GE360.Trading.Observability;

public enum ObservabilityEventKind
{
    RuntimeStarted = 0,
    SignalObserved = 1,
    RiskApproved = 2,
    RiskRejected = 3,
    ExecutionSubmitted = 4,
    ExecutionRejected = 5,
    OrderEvent = 6,
    RecoveryState = 7,
    ProtectionState = 8,
    RuntimeStopped = 9,
    ObservabilityWarning = 10
}

public enum ObservabilitySeverity
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

public sealed record ObservabilityEvent(
    Guid EventId,
    DateTime UtcTime,
    ObservabilityEventKind Kind,
    ObservabilitySeverity Severity,
    string Code,
    string Message,
    string? Symbol,
    string? StrategyId,
    Guid? SignalId,
    IReadOnlyDictionary<string, string> Data)
{
    public static ObservabilityEvent Create(
        DateTime utcTime,
        ObservabilityEventKind kind,
        ObservabilitySeverity severity,
        string code,
        string message,
        string? symbol = null,
        string? strategyId = null,
        Guid? signalId = null,
        IReadOnlyDictionary<string, string>? data = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException(
                "Observability code is required.",
                nameof(code));
        }

        var normalizedUtc = utcTime.Kind == DateTimeKind.Utc
            ? utcTime
            : utcTime.ToUniversalTime();

        return new ObservabilityEvent(
            Guid.NewGuid(),
            normalizedUtc,
            kind,
            severity,
            code.Trim(),
            message?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(symbol)
                ? null
                : symbol.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(strategyId)
                ? null
                : strategyId.Trim(),
            signalId,
            data is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(
                    data,
                    StringComparer.OrdinalIgnoreCase));
    }
}
