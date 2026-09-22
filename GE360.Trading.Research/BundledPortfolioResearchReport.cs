namespace GE360.Trading.Research;

public sealed record BundledPortfolioResearchReport(
    DateTime GeneratedAtUtc,
    string DatasetLabel,
    int TradeArchiveCount,
    int SymbolCount,
    int CalendarSessionCount,
    int SymbolSessionCount,
    IReadOnlyList<string> Symbols,
    IReadOnlyList<DateTime> Sessions,
    PortfolioResearchResult FullSample,
    PortfolioResearchResult InSample,
    PortfolioResearchResult OutOfSample,
    ChronologicalResearchSplit Split,
    IReadOnlyList<WalkForwardEvaluation> WalkForward,
    string Warning);
