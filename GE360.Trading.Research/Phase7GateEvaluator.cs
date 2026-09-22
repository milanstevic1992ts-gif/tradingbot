namespace GE360.Trading.Research;

public sealed record Phase7GateRequirements(
    int MinimumForwardPaperSessions)
{
    public static Phase7GateRequirements Defaults => new(
        MinimumForwardPaperSessions: 20);
}

public sealed record Phase7GateResult(
    bool ReadyForPhase8,
    string Status,
    IReadOnlyList<string> Blockers,
    DatasetQualityAssessment DatasetQuality,
    ResearchValidation HistoricalValidation,
    ForwardPaperSummary ForwardPaper,
    bool LiveSubmissionEnabled)
{
    public static Phase7GateResult Blocked(
        IReadOnlyList<string> blockers,
        DatasetQualityAssessment quality,
        ResearchValidation historical,
        ForwardPaperSummary paper,
        bool liveSubmissionEnabled)
        => new(
            false,
            "BLOCKED",
            blockers,
            quality,
            historical,
            paper,
            liveSubmissionEnabled);

    public static Phase7GateResult Ready(
        DatasetQualityAssessment quality,
        ResearchValidation historical,
        ForwardPaperSummary paper)
        => new(
            true,
            "READY_FOR_PHASE_8",
            Array.Empty<string>(),
            quality,
            historical,
            paper,
            false);
}

public static class Phase7GateEvaluator
{
    public static Phase7GateResult Evaluate(
        ExternalPortfolioResearchReport research,
        ForwardPaperSummary forwardPaper,
        bool liveSubmissionEnabled,
        Phase7GateRequirements? requirements = null)
    {
        ArgumentNullException.ThrowIfNull(research);
        ArgumentNullException.ThrowIfNull(forwardPaper);

        var rules = requirements ?? Phase7GateRequirements.Defaults;
        if (rules.MinimumForwardPaperSessions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requirements));
        }

        var blockers = new List<string>();

        if (!research.Quality.IsAdequateForPhase7)
        {
            blockers.Add("DATASET_QUALITY_NOT_ADEQUATE");
        }

        if (research.FullSample.Validation.Status !=
            ResearchValidationStatus.MinimumSampleReached)
        {
            blockers.Add("HISTORICAL_COUNT_GATE_NOT_REACHED");
        }

        if (research.OutOfSample.BarCount == 0 ||
            research.OutOfSample.SessionCount == 0)
        {
            blockers.Add("OUT_OF_SAMPLE_NOT_EXECUTED");
        }

        if (research.WalkForward.Count == 0 ||
            research.WalkForward.Any(x => x.Test.BarCount == 0))
        {
            blockers.Add("WALK_FORWARD_NOT_EXECUTED");
        }

        if (forwardPaper.QualifyingSessions <
            rules.MinimumForwardPaperSessions)
        {
            blockers.Add("FORWARD_PAPER_OBSERVATION_INCOMPLETE");
        }

        if (forwardPaper.StructuralFailureCount > 0)
        {
            blockers.Add("FORWARD_PAPER_STRUCTURAL_FAILURES_PRESENT");
        }

        if (forwardPaper.LiveSubmissionAttemptCount > 0)
        {
            blockers.Add("LIVE_SUBMISSION_ATTEMPT_DETECTED");
        }

        if (liveSubmissionEnabled)
        {
            blockers.Add("LIVE_SUBMISSION_ENABLED");
        }

        return blockers.Count == 0
            ? Phase7GateResult.Ready(
                research.Quality,
                research.FullSample.Validation,
                forwardPaper)
            : Phase7GateResult.Blocked(
                blockers,
                research.Quality,
                research.FullSample.Validation,
                forwardPaper,
                liveSubmissionEnabled);
    }
}
