namespace GE360.Trading.Research;

public sealed record ResearchReport(
    string Symbol,
    DateTime GeneratedAtUtc,
    string DatasetLabel,
    ResearchBacktestResult FullSample,
    ResearchBacktestResult InSample,
    ResearchBacktestResult OutOfSample,
    ChronologicalResearchSplit Split,
    IReadOnlyList<WalkForwardWindow> WalkForwardWindows,
    string Warning);
