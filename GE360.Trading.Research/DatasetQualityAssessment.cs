using GE360.Trading.Features;

namespace GE360.Trading.Research;

public sealed record DatasetQualityRequirements(
    int MinimumCalendarSessions,
    decimal MinimumWeekdayCoverageRatio,
    decimal MinimumMedianBarsPerSymbolSession,
    int MaximumDuplicateBars)
{
    public static DatasetQualityRequirements Phase7Defaults => new(
        MinimumCalendarSessions: 20,
        MinimumWeekdayCoverageRatio: 0.75m,
        MinimumMedianBarsPerSymbolSession: 300m,
        MaximumDuplicateBars: 0);
}

public sealed record DatasetQualityAssessment(
    bool IsAdequateForPhase7,
    int CalendarSessionCount,
    int SymbolCount,
    int SymbolSessionCount,
    DateTime? FirstSession,
    DateTime? LastSession,
    int ExpectedWeekdaysInSpan,
    decimal WeekdayCoverageRatio,
    decimal MedianBarsPerSymbolSession,
    int DuplicateBarCount,
    string Note)
{
    public static DatasetQualityAssessment Assess(
        IReadOnlyCollection<IntradayBar> bars,
        DatasetQualityRequirements? requirements = null)
    {
        ArgumentNullException.ThrowIfNull(bars);
        var rules = requirements ?? DatasetQualityRequirements.Phase7Defaults;

        if (bars.Count == 0)
        {
            return new DatasetQualityAssessment(
                false, 0, 0, 0, null, null, 0, 0m, 0m, 0,
                "Dataset is empty.");
        }

        var sessions = bars
            .Select(x => x.ExchangeLocalTime.Date)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var symbols = bars
            .Select(x => x.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var symbolSessionGroups = bars
            .GroupBy(x => $"{x.Symbol.ToUpperInvariant()}:{x.ExchangeLocalTime:yyyyMMdd}")
            .ToArray();

        var counts = symbolSessionGroups
            .Select(group => group.Count())
            .OrderBy(x => x)
            .ToArray();

        var medianBars = Median(counts);
        var expectedWeekdays = CountWeekdays(sessions[0], sessions[^1]);
        var coverage = expectedWeekdays > 0
            ? (decimal)sessions.Length / expectedWeekdays
            : 0m;

        var duplicateCount = bars
            .GroupBy(x => $"{x.Symbol.ToUpperInvariant()}:{x.UtcTime:O}")
            .Sum(group => Math.Max(0, group.Count() - 1));

        var adequate =
            sessions.Length >= rules.MinimumCalendarSessions &&
            coverage >= rules.MinimumWeekdayCoverageRatio &&
            medianBars >= rules.MinimumMedianBarsPerSymbolSession &&
            duplicateCount <= rules.MaximumDuplicateBars;

        var note = adequate
            ? "Dataset passed the phase-7 engineering quality gate. This does not establish statistical significance or future profitability."
            : "Dataset failed one or more phase-7 quality checks (continuity, session depth, duplicates, or minimum sessions).";

        return new DatasetQualityAssessment(
            adequate,
            sessions.Length,
            symbols.Length,
            symbolSessionGroups.Length,
            sessions[0],
            sessions[^1],
            expectedWeekdays,
            coverage,
            medianBars,
            duplicateCount,
            note);
    }

    private static int CountWeekdays(DateTime start, DateTime end)
    {
        var count = 0;

        for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                count++;
            }
        }

        return count;
    }

    private static decimal Median(IReadOnlyList<int> ordered)
    {
        if (ordered.Count == 0)
        {
            return 0m;
        }

        var middle = ordered.Count / 2;

        return ordered.Count % 2 == 1
            ? ordered[middle]
            : (ordered[middle - 1] + ordered[middle]) / 2m;
    }
}
