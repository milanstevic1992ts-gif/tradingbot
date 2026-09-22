namespace GE360.Trading.Research;

public sealed record ExternalPortfolioResearchReport(
    DateTime GeneratedAtUtc,
    string DatasetPath,
    DatasetQualityAssessment Quality,
    PortfolioResearchResult FullSample,
    PortfolioResearchResult InSample,
    PortfolioResearchResult OutOfSample,
    ChronologicalResearchSplit Split,
    IReadOnlyList<WalkForwardEvaluation> WalkForward,
    string Warning);
