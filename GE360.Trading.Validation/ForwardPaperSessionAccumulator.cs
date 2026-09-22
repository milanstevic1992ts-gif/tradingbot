namespace GE360.Trading.Validation;

public sealed class ForwardPaperSessionAccumulator
{
    private decimal _netFillQuantity;
    private int _closedTrades;
    private int _structuralFailures;

    public ForwardPaperSessionAccumulator(
        DateTime sessionDate,
        DateTime startedAtUtc,
        decimal startingEquity,
        bool startedLate)
    {
        if (sessionDate == default)
        {
            throw new ArgumentException("Session date is required.", nameof(sessionDate));
        }

        if (startedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("StartedAtUtc must be UTC.", nameof(startedAtUtc));
        }

        SessionDate = sessionDate.Date;
        StartedAtUtc = startedAtUtc;
        StartingEquity = startingEquity;

        if (startedLate)
        {
            _structuralFailures++;
        }
    }

    public DateTime SessionDate { get; }
    public DateTime StartedAtUtc { get; }
    public decimal StartingEquity { get; }
    public decimal NetFillQuantity => _netFillQuantity;
    public int ClosedTrades => _closedTrades;
    public int StructuralFailureCount => _structuralFailures;

    public void RecordFill(decimal fillQuantity)
    {
        if (fillQuantity == 0m)
        {
            return;
        }

        var previous = _netFillQuantity;
        var next = previous + fillQuantity;

        if (previous != 0m && next == 0m)
        {
            _closedTrades++;
        }
        else if (previous != 0m &&
                 next != 0m &&
                 Math.Sign(previous) != Math.Sign(next))
        {
            // A single fill crossed through flat into the opposite direction.
            // GE360 strategies should close then reopen through separate approval cycles.
            _closedTrades++;
            _structuralFailures++;
        }

        _netFillQuantity = next;
    }

    public void RecordStructuralFailure()
        => _structuralFailures++;

    public ForwardPaperSession Complete(
        DateTime endedAtUtc,
        decimal endingEquity,
        bool portfolioFlat,
        bool endedTooEarly,
        int liveSubmissionAttemptCount = 0,
        string note = "")
    {
        if (endedAtUtc.Kind != DateTimeKind.Utc ||
            endedAtUtc <= StartedAtUtc)
        {
            throw new ArgumentException(
                "EndedAtUtc must be UTC and later than StartedAtUtc.",
                nameof(endedAtUtc));
        }

        if (liveSubmissionAttemptCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(liveSubmissionAttemptCount));
        }

        var failures = _structuralFailures;

        if (endedTooEarly)
        {
            failures++;
        }

        var fillsFlat = _netFillQuantity == 0m;
        if (portfolioFlat != fillsFlat)
        {
            failures++;
        }

        return new ForwardPaperSession(
            SessionDate,
            StartedAtUtc,
            endedAtUtc,
            _closedTrades,
            endingEquity - StartingEquity,
            portfolioFlat && fillsFlat,
            failures,
            liveSubmissionAttemptCount,
            note ?? string.Empty);
    }
}
