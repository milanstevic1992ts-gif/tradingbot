using GE360.Trading.Research;
using GE360.Trading.Validation;
using NUnit.Framework;

namespace GE360.Trading.Tests;

[TestFixture]
public sealed class Phase7GateTests
{
    [Test]
    public void ForwardPaperStorePersistsAndRejectsDuplicateSession()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ge360-paper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var path = Path.Combine(directory, "paper.json");
            var store = new ForwardPaperObservationStore();

            var session = QualifyingSession(new DateTime(2026, 9, 1));
            store.Add(session);
            store.Save(path);

            var loaded = ForwardPaperObservationStore.Load(path);

            Assert.That(loaded.Sessions.Count, Is.EqualTo(1));
            Assert.That(loaded.Sessions[0].Qualifies, Is.True);
            Assert.Throws<InvalidOperationException>(() =>
                loaded.Add(session));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void ForwardPaperStoreRejectsDuplicateDatesAtLoadBoundary()
    {
        var session = QualifyingSession(new DateTime(2026, 9, 1));

        Assert.Throws<InvalidOperationException>(() =>
            new ForwardPaperObservationStore(new[] { session, session }));
    }

    [Test]
    public void Phase7ProgressReportsMissingHistoricalReportWithoutThrowing()
    {
        var paper = ForwardPaperSummary.Assess(
            Array.Empty<ForwardPaperSession>());

        var progress = Phase7ProgressReport.Build(
            research: null,
            paper,
            liveSubmissionEnabled: false);

        Assert.That(progress.Ready, Is.False);
        Assert.That(progress.HistoricalReportPresent, Is.False);
        Assert.That(
            progress.Blockers,
            Does.Contain("RESEARCH_REPORT_MISSING"));
        Assert.That(
            progress.Blockers,
            Does.Contain("FORWARD_PAPER_OBSERVATION_INCOMPLETE"));
    }

    [Test]
    public void Phase7ProgressCanBecomeReadyWhenAllRequirementsPass()
    {
        var paper = ForwardPaperSummary.Assess(
            BusinessDays(new DateTime(2026, 6, 1), 20)
                .Select(QualifyingSession)
                .ToArray());

        var progress = Phase7ProgressReport.Build(
            AdequateResearchReport(),
            paper,
            liveSubmissionEnabled: false);

        Assert.That(progress.Ready, Is.True);
        Assert.That(progress.DatasetAdequate, Is.True);
        Assert.That(progress.HistoricalCountGateReached, Is.True);
        Assert.That(progress.OutOfSampleExecuted, Is.True);
        Assert.That(progress.WalkForwardExecuted, Is.True);
        Assert.That(progress.QualifyingForwardPaperSessions, Is.EqualTo(20));
        Assert.That(progress.Blockers, Is.Empty);
    }

    [Test]
    public void Phase7GateBlocksIncompleteForwardPaper()
    {
        var research = AdequateResearchReport();
        var paper = ForwardPaperSummary.Assess(
            Enumerable.Range(1, 5)
                .Select(day => QualifyingSession(new DateTime(2026, 9, day)))
                .ToArray());

        var result = Phase7GateEvaluator.Evaluate(
            research,
            paper,
            liveSubmissionEnabled: false);

        Assert.That(result.ReadyForPhase8, Is.False);
        Assert.That(
            result.Blockers,
            Does.Contain("FORWARD_PAPER_OBSERVATION_INCOMPLETE"));
    }

    [Test]
    public void Phase7GateBlocksLiveSubmissionEvenWhenEverythingElsePasses()
    {
        var research = AdequateResearchReport();
        var paper = ForwardPaperSummary.Assess(
            Enumerable.Range(1, 20)
                .Select(day => QualifyingSession(new DateTime(2026, 8, day)))
                .ToArray());

        var result = Phase7GateEvaluator.Evaluate(
            research,
            paper,
            liveSubmissionEnabled: true);

        Assert.That(result.ReadyForPhase8, Is.False);
        Assert.That(
            result.Blockers,
            Does.Contain("LIVE_SUBMISSION_ENABLED"));
    }

    [Test]
    public void Phase7GateBlocksInadequateDatasetEvenWithEnoughTrades()
    {
        var research = AdequateResearchReport() with
        {
            Quality = AdequateResearchReport().Quality with
            {
                IsAdequateForPhase7 = false,
                Note = "fragmented test dataset"
            }
        };

        var paper = ForwardPaperSummary.Assess(
            Enumerable.Range(1, 20)
                .Select(day => QualifyingSession(new DateTime(2026, 7, day)))
                .ToArray());

        var result = Phase7GateEvaluator.Evaluate(
            research,
            paper,
            liveSubmissionEnabled: false);

        Assert.That(result.ReadyForPhase8, Is.False);
        Assert.That(
            result.Blockers,
            Does.Contain("DATASET_QUALITY_NOT_ADEQUATE"));
    }

    [Test]
    public void Phase7GateCanReachReadyOnlyWhenAllEngineeringChecksPass()
    {
        var research = AdequateResearchReport();
        var paper = ForwardPaperSummary.Assess(
            BusinessDays(new DateTime(2026, 6, 1), 20)
                .Select(QualifyingSession)
                .ToArray());

        var result = Phase7GateEvaluator.Evaluate(
            research,
            paper,
            liveSubmissionEnabled: false);

        Assert.That(result.ReadyForPhase8, Is.True);
        Assert.That(result.Status, Is.EqualTo("READY_FOR_PHASE_8"));
        Assert.That(result.Blockers, Is.Empty);
    }

    private static ExternalPortfolioResearchReport AdequateResearchReport()
    {
        var quality = new DatasetQualityAssessment(
            IsAdequateForPhase7: true,
            CalendarSessionCount: 40,
            SymbolCount: 5,
            SymbolSessionCount: 200,
            FirstSession: new DateTime(2026, 1, 2),
            LastSession: new DateTime(2026, 2, 27),
            ExpectedWeekdaysInSpan: 41,
            WeekdayCoverageRatio: 0.975m,
            MedianBarsPerSymbolSession: 390m,
            DuplicateBarCount: 0,
            Note: "adequate");

        var metrics = new ResearchMetrics(
            TradeCount: 40,
            WinningTrades: 20,
            LosingTrades: 20,
            WinRate: 0.5m,
            GrossPnl: 100m,
            NetPnl: 50m,
            TotalCosts: 50m,
            AverageNetPnl: 1.25m,
            MaxDrawdownPercent: 0.01m,
            ReturnPercent: 0.0005m,
            ProfitFactor: 1.1m);

        var full = new PortfolioResearchResult(
            BarCount: 50_000,
            CalendarSessionCount: 40,
            SymbolCount: 5,
            SymbolSessionCount: 200,
            Trades: Array.Empty<ResearchTrade>(),
            Metrics: metrics,
            Validation: ResearchValidation.Assess(
                40,
                40,
                datasetAdequate: true));

        var inSample = full with
        {
            BarCount = 35_000,
            CalendarSessionCount = 28
        };

        var outOfSample = full with
        {
            BarCount = 15_000,
            CalendarSessionCount = 12
        };

        var trainingSessions = BusinessDays(
            new DateTime(2026, 1, 2),
            20).ToArray();

        var testSessions = BusinessDays(
            new DateTime(2026, 2, 2),
            5).ToArray();

        var window = new WalkForwardWindow(
            0,
            trainingSessions,
            testSessions);

        var evaluation = new WalkForwardEvaluation(
            0,
            window.TrainingPeriod,
            window.TestPeriod,
            inSample,
            outOfSample);

        var split = new ChronologicalResearchSplit(
            new ResearchPeriod(
                new DateTime(2026, 1, 2),
                new DateTime(2026, 2, 10)),
            new ResearchPeriod(
                new DateTime(2026, 2, 11),
                new DateTime(2026, 2, 27)),
            trainingSessions,
            testSessions);

        return new ExternalPortfolioResearchReport(
            DateTime.UtcNow,
            "/tmp/adequate.csv",
            quality,
            full,
            inSample,
            outOfSample,
            split,
            new[] { evaluation },
            "synthetic gate test only");
    }

    private static ForwardPaperSession QualifyingSession(DateTime date)
    {
        var started = DateTime.SpecifyKind(
            date.Date.AddHours(13).AddMinutes(30),
            DateTimeKind.Utc);

        var ended = DateTime.SpecifyKind(
            date.Date.AddHours(20),
            DateTimeKind.Utc);

        return new ForwardPaperSession(
            date.Date,
            started,
            ended,
            ClosedTrades: 1,
            NetPnl: 0m,
            EndedFlat: true,
            StructuralFailureCount: 0,
            LiveSubmissionAttemptCount: 0,
            Note: "paper");
    }

    private static IEnumerable<DateTime> BusinessDays(
        DateTime start,
        int count)
    {
        var date = start.Date;
        var emitted = 0;

        while (emitted < count)
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                yield return date;
                emitted++;
            }

            date = date.AddDays(1);
        }
    }
}
