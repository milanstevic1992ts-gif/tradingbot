namespace GE360.Trading.LeanAdapter;

public sealed record LeanRuntimeSubmissionDecision(
    bool Allowed,
    string Code,
    string Reason);

public static class LeanRuntimeSubmissionPolicy
{
    public static LeanRuntimeSubmissionDecision Evaluate(
        bool liveMode,
        string? liveModeBrokerage,
        LeanExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!liveMode)
        {
            return new LeanRuntimeSubmissionDecision(
                true,
                "BACKTEST_MODE",
                "Backtest submission is allowed.");
        }

        var isPaperBrokerage = string.Equals(
            liveModeBrokerage?.Trim(),
            "PaperBrokerage",
            StringComparison.OrdinalIgnoreCase);

        if (isPaperBrokerage)
        {
            return options.AllowPaperBrokerageSubmission
                ? new LeanRuntimeSubmissionDecision(
                    true,
                    "LEAN_PAPER_BROKERAGE",
                    "LEAN PaperBrokerage submission is explicitly enabled.")
                : new LeanRuntimeSubmissionDecision(
                    false,
                    "PAPER_SUBMISSION_DISABLED",
                    "GE360 paper submission is disabled unless LEAN is explicitly running PaperBrokerage and the paper option is enabled.");
        }

        return options.EnableLiveSubmission
            ? new LeanRuntimeSubmissionDecision(
                true,
                "LIVE_SUBMISSION_EXPLICITLY_ENABLED",
                "Real-broker live submission is explicitly enabled.")
            : new LeanRuntimeSubmissionDecision(
                false,
                "LIVE_SUBMISSION_DISABLED",
                "GE360 blocks real-broker live submission by default.");
    }
}
