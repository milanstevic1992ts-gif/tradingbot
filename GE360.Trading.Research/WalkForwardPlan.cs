namespace GE360.Trading.Research;

public sealed record WalkForwardWindow(
    int Index,
    IReadOnlyList<DateTime> TrainingSessions,
    IReadOnlyList<DateTime> TestSessions)
{
    public ResearchPeriod TrainingPeriod =>
        new(TrainingSessions[0], TrainingSessions[^1]);

    public ResearchPeriod TestPeriod =>
        new(TestSessions[0], TestSessions[^1]);
}

public static class WalkForwardPlan
{
    public static IReadOnlyList<WalkForwardWindow> Build(
        IEnumerable<DateTime> sessions,
        int trainingSessions,
        int testSessions,
        int? stepSessions = null)
    {
        if (trainingSessions <= 0 || testSessions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(trainingSessions));
        }

        var step = stepSessions ?? testSessions;
        if (step <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepSessions));
        }

        var ordered = sessions
            .Select(x => x.Date)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var windows = new List<WalkForwardWindow>();
        var index = 0;

        for (var start = 0;
             start + trainingSessions + testSessions <= ordered.Length;
             start += step)
        {
            windows.Add(new WalkForwardWindow(
                index++,
                ordered[start..(start + trainingSessions)],
                ordered[(start + trainingSessions)..(start + trainingSessions + testSessions)]));
        }

        return windows;
    }
}
