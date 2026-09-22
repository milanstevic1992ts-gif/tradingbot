namespace GE360.Trading.Observability;

public sealed record RuntimePositionView(
    string Symbol,
    decimal Quantity,
    decimal AveragePrice,
    decimal MarketPrice,
    decimal Notional);

public sealed record RuntimeObservabilitySnapshot(
    DateTime UtcTime,
    string TradingState,
    decimal Equity,
    decimal Cash,
    decimal GrossExposure,
    decimal DailyPnl,
    decimal DailyLossPercent,
    decimal DrawdownPercent,
    int OpenPositionCount,
    int OpenOrderCount,
    IReadOnlyList<RuntimePositionView> Positions,
    bool ProtectionHalted,
    string ProtectionHaltReason,
    string RecoveryMode,
    bool RecoveryHealthy,
    string RecoveryDetail,
    bool JournalHealthy,
    string JournalError,
    string JournalPath,
    string RecentEventsPath);
