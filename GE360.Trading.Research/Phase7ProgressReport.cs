using GE360.Trading.Validation;

namespace GE360.Trading.Research;

public sealed record Phase7ProgressReport(
    DateTime GeneratedAtUtc,
    bool HistoricalReportPresent,
    bool DatasetAdequate,
    int HistoricalCalendarSessions,
    int HistoricalClosedTrades,
    bool HistoricalCountGateReached,
    bool OutOfSampleExecuted,
    bool WalkForwardExecuted,
    int WalkForwardWindowCount,
    int ForwardPaperSessions,
    int QualifyingForwardPaperSessions,
    int RequiredForwardPaperSessions,
    int ForwardPaperStructuralFailures,
    int ForwardPaperLiveSubmissionAttempts,
    bool LiveSubmissionEnabled,
    bool Ready,
    IReadOnlyList<string> Blockers)
{
    public static Phase7ProgressReport Build(
        ExternalPortfolioResearchReport? research,
        ForwardPaperSummary paper,
        bool liveSubmissionEnabled)
    {
        ArgumentNullException.ThrowIfNull(paper);

        var blockers = new List<string>();

        var reportPresent = research is not null;
        var datasetAdequate =
            research?.Quality.IsAdequateForPhase7 == true;

        var historicalSessions =
            research?.FullSample.CalendarSessionCount ?? 0;

        var historicalTrades =
            research?.FullSample.Metrics.TradeCount ?? 0;

        var countGate =
            research?.FullSample.Validation.Status ==
            ResearchValidationStatus.MinimumSampleReached;

        var oosExecuted =
            research is not null &&
            research.OutOfSample.BarCount > 0 &&
            research.OutOfSample.CalendarSessionCount > 0;

        var walkForwardExecuted =
            research is not null &&
            research.WalkForward.Count > 0 &&
            research.WalkForward.All(window =>
                window.Test.BarCount > 0 &&
                window.Test.CalendarSessionCount > 0);

        if (!reportPresent)
        {
            blockers.Add("RESEARCH_REPORT_MISSING");
        }
        else
        {
            if (!datasetAdequate)
            {
                blockers.Add("DATASET_QUALITY_NOT_ADEQUATE");
            }

            if (!countGate)
            {
                blockers.Add("HISTORICAL_COUNT_GATE_NOT_REACHED");
            }

            if (!oosExecuted)
            {
                blockers.Add("OUT_OF_SAMPLE_NOT_EXECUTED");
            }

            if (!walkForwardExecuted)
            {
                blockers.Add("WALK_FORWARD_NOT_EXECUTED");
            }
        }

        if (paper.QualifyingSessions <
            paper.MinimumRequiredSessions)
        {
            blockers.Add("FORWARD_PAPER_OBSERVATION_INCOMPLETE");
        }

        if (paper.StructuralFailureCount > 0)
        {
            blockers.Add("FORWARD_PAPER_STRUCTURAL_FAILURES_PRESENT");
        }

        if (paper.LiveSubmissionAttemptCount > 0)
        {
            blockers.Add("LIVE_SUBMISSION_ATTEMPT_DETECTED");
        }

        if (liveSubmissionEnabled)
        {
            blockers.Add("LIVE_SUBMISSION_ENABLED");
        }

        return new Phase7ProgressReport(
            DateTime.UtcNow,
            reportPresent,
            datasetAdequate,
            historicalSessions,
            historicalTrades,
            countGate,
            oosExecuted,
            walkForwardExecuted,
            research?.WalkForward.Count ?? 0,
            paper.TotalSessions,
            paper.QualifyingSessions,
            paper.MinimumRequiredSessions,
            paper.StructuralFailureCount,
            paper.LiveSubmissionAttemptCount,
            liveSubmissionEnabled,
            blockers.Count == 0,
            blockers);
    }
}
