namespace GE360.Trading.Research;

public sealed record ResearchPeriod(
    DateTime StartSession,
    DateTime EndSession);

public sealed record ChronologicalResearchSplit(
    ResearchPeriod InSample,
    ResearchPeriod OutOfSample,
    IReadOnlyList<DateTime> InSampleSessions,
    IReadOnlyList<DateTime> OutOfSampleSessions);

public static class ResearchSplitBuilder
{
    public static ChronologicalResearchSplit Chronological(
        IEnumerable<DateTime> sessions,
        decimal inSampleFraction = 0.70m)
    {
        if (inSampleFraction is <= 0m or >= 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(inSampleFraction));
        }

        var ordered = sessions
            .Select(x => x.Date)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        if (ordered.Length < 2)
        {
            throw new ArgumentException("At least two sessions are required.", nameof(sessions));
        }

        var splitIndex = Math.Clamp(
            (int)Math.Floor(ordered.Length * inSampleFraction),
            1,
            ordered.Length - 1);

        var train = ordered[..splitIndex];
        var test = ordered[splitIndex..];

        return new ChronologicalResearchSplit(
            new ResearchPeriod(train[0], train[^1]),
            new ResearchPeriod(test[0], test[^1]),
            train,
            test);
    }
}
