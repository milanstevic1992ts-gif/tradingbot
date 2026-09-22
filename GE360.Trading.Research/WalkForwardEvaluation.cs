using GE360.Trading.Features;
using GE360.Trading.Strategies;

namespace GE360.Trading.Research;

public sealed record WalkForwardEvaluation(
    int Index,
    ResearchPeriod TrainingPeriod,
    ResearchPeriod TestPeriod,
    PortfolioResearchResult Training,
    PortfolioResearchResult Test);

public static class WalkForwardEvaluator
{
    public static IReadOnlyList<WalkForwardEvaluation> Evaluate(
        PortfolioResearchRunner runner,
        IReadOnlyCollection<IntradayBar> bars,
        IReadOnlyList<WalkForwardWindow> windows,
        decimal initialEquity = 100_000m,
        ResearchCostModel? costs = null,
        OpeningRangeMomentumConfig? strategyConfig = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(windows);

        var output = new List<WalkForwardEvaluation>();

        foreach (var window in windows)
        {
            var trainingDates = window.TrainingSessions.ToHashSet();
            var testDates = window.TestSessions.ToHashSet();

            var trainingBars = bars
                .Where(x => trainingDates.Contains(x.ExchangeLocalTime.Date))
                .ToArray();

            var testBars = bars
                .Where(x => testDates.Contains(x.ExchangeLocalTime.Date))
                .ToArray();

            output.Add(new WalkForwardEvaluation(
                window.Index,
                window.TrainingPeriod,
                window.TestPeriod,
                runner.Run(
                    trainingBars,
                    initialEquity,
                    costs,
                    strategyConfig,
                    datasetAdequate: false),
                runner.Run(
                    testBars,
                    initialEquity,
                    costs,
                    strategyConfig,
                    datasetAdequate: false)));
        }

        return output;
    }
}
