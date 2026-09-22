namespace GE360.Trading.Research;

public sealed record ForwardPaperSession(
    DateTime SessionDate,
    DateTime StartedAtUtc,
    DateTime EndedAtUtc,
    int ClosedTrades,
    decimal NetPnl,
    bool EndedFlat,
    int StructuralFailureCount,
    int LiveSubmissionAttemptCount,
    string Note)
{
    public bool Qualifies =>
        EndedAtUtc > StartedAtUtc &&
        EndedFlat &&
        StructuralFailureCount == 0 &&
        LiveSubmissionAttemptCount == 0;
}

public sealed record ForwardPaperSummary(
    int TotalSessions,
    int QualifyingSessions,
    int InvalidSessions,
    int ClosedTrades,
    decimal NetPnl,
    int StructuralFailureCount,
    int LiveSubmissionAttemptCount,
    DateTime? FirstSession,
    DateTime? LastSession,
    bool MeetsMinimumObservation,
    int MinimumRequiredSessions,
    string Note)
{
    public static ForwardPaperSummary Assess(
        IReadOnlyCollection<ForwardPaperSession> sessions,
        int minimumRequiredSessions = 20)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (minimumRequiredSessions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumRequiredSessions));
        }

        var ordered = sessions
            .OrderBy(x => x.SessionDate.Date)
            .ToArray();

        var qualifying = ordered.Count(x => x.Qualifies);
        var failures = ordered.Sum(x => x.StructuralFailureCount);
        var liveAttempts = ordered.Sum(x => x.LiveSubmissionAttemptCount);
        var meets = qualifying >= minimumRequiredSessions &&
                    failures == 0 &&
                    liveAttempts == 0;

        return new ForwardPaperSummary(
            ordered.Length,
            qualifying,
            ordered.Length - qualifying,
            ordered.Sum(x => x.ClosedTrades),
            ordered.Sum(x => x.NetPnl),
            failures,
            liveAttempts,
            ordered.Length == 0 ? null : ordered[0].SessionDate.Date,
            ordered.Length == 0 ? null : ordered[^1].SessionDate.Date,
            meets,
            minimumRequiredSessions,
            meets
                ? "Forward-paper engineering observation requirement reached. This is not evidence of future profitability."
                : "Forward-paper observation requirement not yet reached.");
    }
}
