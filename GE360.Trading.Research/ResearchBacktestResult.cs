namespace GE360.Trading.Research;

public sealed record ResearchBacktestResult(
    int BarCount,
    int SessionCount,
    IReadOnlyList<ResearchTrade> Trades,
    ResearchMetrics Metrics,
    ResearchValidation Validation);
