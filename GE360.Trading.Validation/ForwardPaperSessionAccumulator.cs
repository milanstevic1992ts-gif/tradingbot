namespace GE360.Trading.Validation;

public sealed class ForwardPaperSessionAccumulator
{
    private readonly Dictionary<string, decimal> _netFillQuantities = new(StringComparer.OrdinalIgnoreCase);
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
    // Aggregate quantity is retained for diagnostics only; flat/trade accounting is symbol-aware.
    public decimal NetFillQuantity => _netFillQuantities.Values.Sum();
    public int OpenSymbolPositionCount => _netFillQuantities.Count;
    public int ClosedTrades => _closedTrades;
    public int StructuralFailureCount => _structuralFailures;

    public void RecordFill(string symbol, decimal fillQuantity)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Fill symbol is required.", nameof(symbol));
        }

        if (fillQuantity == 0m)
        {
            return;
        }

        _netFillQuantities.TryGetValue(symbol, out var previous);
        var next = previous + fillQuantity;

        if (previous != 0m && next == 0m)
        {
            _closedTrades++;
        }
        else if (previous != 0m &&
                 next != 0m &&
                 Math.Sign(previous) != Math.Sign(next))
        {
            // A single fill crossed through flat into the opposite direction
            // for the same symbol. GE360 strategies should close then reopen
            // through separate approval cycles.
            _closedTrades++;
            _structuralFailures++;
        }

        if (next == 0m)
        {
            _netFillQuantities.Remove(symbol);
        }
        else
        {
            _netFillQuantities[symbol] = next;
        }
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

        var fillsFlat = _netFillQuantities.Count == 0;
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
