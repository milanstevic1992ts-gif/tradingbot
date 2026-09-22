namespace GE360.Trading.Research;

public sealed record PortfolioResearchResult(
    int BarCount,
    int CalendarSessionCount,
    int SymbolCount,
    int SymbolSessionCount,
    IReadOnlyList<ResearchTrade> Trades,
    ResearchMetrics Metrics,
    ResearchValidation Validation);
