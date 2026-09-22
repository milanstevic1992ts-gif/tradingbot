using GE360.Trading.Features;
using GE360.Trading.Research;
using NUnit.Framework;

namespace GE360.Trading.Tests;

[TestFixture]
public sealed class ResearchInfrastructureTests
{
    [Test]
    public void LoadsBundledLeanMinuteTradeArchive()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(
            root,
            "Data",
            "equity",
            "usa",
            "minute",
            "spy",
            "20131007_trade.zip");

        var bars = LeanMinuteTradeDataLoader.LoadTradeZip(path, "SPY");

        Assert.That(bars.Count, Is.GreaterThan(100));
        Assert.That(bars.All(x => x.Symbol == "SPY"), Is.True);
        Assert.That(bars.All(x => x.Close > 10m && x.Close < 1_000m), Is.True);
        Assert.That(bars.All(x => x.ExchangeLocalTime.Date == new DateTime(2013, 10, 7)), Is.True);
    }

    [Test]
    public void ChronologicalSplitNeverLeaksFutureSessionsIntoTraining()
    {
        var sessions = Enumerable.Range(1, 10)
            .Select(day => new DateTime(2026, 1, day))
            .ToArray();

        var split = ResearchSplitBuilder.Chronological(sessions, 0.60m);

        Assert.That(split.InSampleSessions, Has.Count.EqualTo(6));
        Assert.That(split.OutOfSampleSessions, Has.Count.EqualTo(4));
        Assert.That(
            split.InSampleSessions.Max(),
            Is.LessThan(split.OutOfSampleSessions.Min()));
    }

    [Test]
    public void WalkForwardWindowsRemainChronologicalAndNonOverlappingWithinWindow()
    {
        var sessions = Enumerable.Range(1, 10)
            .Select(day => new DateTime(2026, 2, day))
            .ToArray();

        var windows = WalkForwardPlan.Build(
            sessions,
            trainingSessions: 4,
            testSessions: 2,
            stepSessions: 2);

        Assert.That(windows, Has.Count.EqualTo(3));

        foreach (var window in windows)
        {
            Assert.That(
                window.TrainingSessions.Max(),
                Is.LessThan(window.TestSessions.Min()));
        }
    }

    [Test]
    public void ExplicitCostsReduceNetPnl()
    {
        var bars = BuildProfitableSyntheticSession();
        var runner = new OpeningRangeResearchRunner();

        var zeroCost = runner.Run(
            bars,
            costs: new ResearchCostModel(0m, 0m));

        var withCosts = runner.Run(
            bars,
            costs: new ResearchCostModel(0.50m, 0.0005m));

        Assert.That(zeroCost.Trades.Count, Is.EqualTo(1));
        Assert.That(withCosts.Trades.Count, Is.EqualTo(1));
        Assert.That(withCosts.Metrics.TotalCosts, Is.GreaterThan(0m));
        Assert.That(withCosts.Metrics.NetPnl, Is.LessThan(zeroCost.Metrics.NetPnl));
    }

    [Test]
    public void ResearchRunnerForcesIntradayFlatten()
    {
        var bars = BuildFlattenSyntheticSession();
        var runner = new OpeningRangeResearchRunner();

        var result = runner.Run(
            bars,
            costs: new ResearchCostModel(0m, 0m));

        Assert.That(result.Trades.Count, Is.EqualTo(1));
        Assert.That(result.Trades[0].ExitReason, Is.EqualTo("intraday-flatten"));
        Assert.That(
            result.Trades[0].ExitTimeUtc,
            Is.GreaterThan(result.Trades[0].EntryTimeUtc));
    }

    [Test]
    public void AlpacaPageParserParsesMultiSymbolBarsAndNextToken()
    {
        const string json = """
        {
          "bars": {
            "AAPL": [
              {
                "t": "2026-09-01T13:30:00Z",
                "o": 230.10,
                "h": 230.40,
                "l": 229.90,
                "c": 230.25,
                "v": 120000
              }
            ],
            "SPY": [
              {
                "t": "2026-09-01T13:30:00Z",
                "o": 650.10,
                "h": 650.30,
                "l": 649.80,
                "c": 650.20,
                "v": 250000
              }
            ]
          },
          "next_page_token": "NEXT123"
        }
        """;

        var page = AlpacaHistoricalBarPageParser.Parse(json);

        Assert.That(page.Bars.Count, Is.EqualTo(2));
        Assert.That(page.Bars.Select(x => x.Symbol).OrderBy(x => x).ToArray(),
            Is.EqualTo(new[] { "AAPL", "SPY" }));
        Assert.That(page.NextPageToken, Is.EqualTo("NEXT123"));
        Assert.That(page.Bars.All(x => x.ExchangeLocalTime.Hour == 9), Is.True);
    }

    [Test]
    public void AlpacaRequestUriUsesOneMinuteBarsAndPagination()
    {
        var uri = AlpacaHistoricalDownloader.BuildRequestUri(
            new AlpacaHistoricalDownloadRequest(
                new[] { "SPY", "AAPL" },
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero),
                Feed: "iex",
                Adjustment: "all"),
            "TOKEN");

        var text = uri.ToString();

        Assert.That(text, Does.Contain("timeframe=1Min"));
        Assert.That(text, Does.Contain("symbols=SPY%2CAAPL"));
        Assert.That(text, Does.Contain("feed=iex"));
        Assert.That(text, Does.Contain("adjustment=all"));
        Assert.That(text, Does.Contain("page_token=TOKEN"));
    }

    [Test]
    public void ExternalCsvLoaderParsesProviderNeutralSchema()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ge360-csv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var path = Path.Combine(directory, "bars.csv");
            File.WriteAllText(
                path,
                "timestamp_utc,symbol,open,high,low,close,volume\n" +
                "2026-09-01T13:30:00Z,SPY,500.10,500.30,499.95,500.20,152340\n" +
                "2026-09-01T13:31:00Z,SPY,500.20,500.40,500.10,500.35,121000\n");

            var bars = ExternalMinuteCsvLoader.Load(path);

            Assert.That(bars.Count, Is.EqualTo(2));
            Assert.That(bars[0].Symbol, Is.EqualTo("SPY"));
            Assert.That(bars[0].UtcTime.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(bars[0].ExchangeLocalTime.Hour, Is.EqualTo(9));
            Assert.That(bars[0].ExchangeLocalTime.Minute, Is.EqualTo(30));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void DatasetQualityGateAcceptsContiguousMinuteHistory()
    {
        var bars = BuildContiguousQualityDataset();
        var quality = DatasetQualityAssessment.Assess(bars);

        Assert.That(quality.IsAdequateForPhase7, Is.True);
        Assert.That(quality.CalendarSessionCount, Is.EqualTo(20));
        Assert.That(quality.MedianBarsPerSymbolSession, Is.EqualTo(300m));
        Assert.That(quality.DuplicateBarCount, Is.Zero);
    }

    [Test]
    public void BundledEquityDatasetMapsAllAvailableArchives()
    {
        var root = FindRepositoryRoot();
        var dataset = BundledEquityDataset.Load(Path.Combine(
            root,
            "Data",
            "equity",
            "usa",
            "minute"));

        Assert.That(dataset.TradeArchiveCount, Is.GreaterThanOrEqualTo(41));
        Assert.That(dataset.Symbols.Count, Is.GreaterThanOrEqualTo(10));
        Assert.That(dataset.CalendarSessions.Count, Is.GreaterThanOrEqualTo(20));
        Assert.That(dataset.SymbolSessionCount, Is.GreaterThanOrEqualTo(41));
    }

    [Test]
    public void CountGateCannotValidateFragmentedDataset()
    {
        var validation = ResearchValidation.Assess(
            sessionCount: 20,
            tradeCount: 30,
            datasetAdequate: false);

        Assert.That(
            validation.Status,
            Is.EqualTo(ResearchValidationStatus.EngineeringSampleOnly));
        Assert.That(validation.DatasetAdequate, Is.False);
    }

    [Test]
    public void PortfolioRunnerSharesRiskAcrossSymbolsAndFinishesFlat()
    {
        var bars = BuildTwoSymbolPortfolioSession();
        var runner = new PortfolioResearchRunner();

        var result = runner.Run(
            bars,
            costs: new ResearchCostModel(0m, 0m),
            datasetAdequate: false);

        Assert.That(result.SymbolCount, Is.EqualTo(2));
        Assert.That(result.SymbolSessionCount, Is.EqualTo(2));
        Assert.That(result.Trades.Count, Is.EqualTo(2));
        Assert.That(result.Trades.Select(x => x.Symbol).Distinct().Count(), Is.EqualTo(2));
    }

    [Test]
    public void SmallBundledDatasetIsClassifiedAsSmokeOnly()
    {
        var validation = ResearchValidation.Assess(
            sessionCount: 5,
            tradeCount: 4);

        Assert.That(validation.Status, Is.EqualTo(ResearchValidationStatus.SmokeOnly));
        Assert.That(validation.Note, Does.Contain("Smoke-test"));
    }

    private static IReadOnlyList<IntradayBar> BuildProfitableSyntheticSession()
    {
        var session = new DateTime(2026, 3, 2);
        var bars = new List<IntradayBar>();

        for (var minute = 0; minute < 15; minute++)
        {
            bars.Add(Bar(
                session,
                9,
                30 + minute,
                100m,
                101m,
                99.5m,
                100m,
                1_000m));
        }

        bars.Add(Bar(
            session,
            9,
            45,
            101.5m,
            102.2m,
            101.4m,
            102m,
            3_000m));

        bars.Add(Bar(
            session,
            9,
            46,
            102m,
            106m,
            101.8m,
            105m,
            1_500m));

        return bars;
    }

    private static IReadOnlyList<IntradayBar> BuildFlattenSyntheticSession()
    {
        var session = new DateTime(2026, 3, 3);
        var bars = new List<IntradayBar>();

        for (var minute = 0; minute < 15; minute++)
        {
            bars.Add(Bar(
                session,
                9,
                30 + minute,
                100m,
                101m,
                99.5m,
                100m,
                1_000m));
        }

        bars.Add(Bar(
            session,
            9,
            45,
            101.5m,
            102.2m,
            101.4m,
            102m,
            3_000m));

        bars.Add(Bar(
            session,
            15,
            55,
            102m,
            102.4m,
            101.6m,
            102.2m,
            1_500m));

        return bars;
    }

    private static IReadOnlyList<IntradayBar> BuildContiguousQualityDataset()
    {
        var bars = new List<IntradayBar>();
        var date = new DateTime(2026, 4, 1);
        var sessions = 0;

        while (sessions < 20)
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                for (var minute = 0; minute < 300; minute++)
                {
                    var local = DateTime.SpecifyKind(
                        date.Date.AddHours(9).AddMinutes(30 + minute),
                        DateTimeKind.Unspecified);

                    var utc = DateTime.SpecifyKind(
                        local.AddHours(4),
                        DateTimeKind.Utc);

                    bars.Add(new IntradayBar(
                        "SPY",
                        utc,
                        local,
                        100m,
                        100.2m,
                        99.8m,
                        100m,
                        10_000m));
                }

                sessions++;
            }

            date = date.AddDays(1);
        }

        return bars;
    }

    private static IReadOnlyList<IntradayBar> BuildTwoSymbolPortfolioSession()
    {
        var session = new DateTime(2026, 3, 4);
        var bars = new List<IntradayBar>();

        foreach (var symbol in new[] { "SPY", "AAPL" })
        {
            for (var minute = 0; minute < 15; minute++)
            {
                bars.Add(Bar(
                    symbol,
                    session,
                    9,
                    30 + minute,
                    100m,
                    101m,
                    99.5m,
                    100m,
                    1_000m));
            }

            bars.Add(Bar(
                symbol,
                session,
                9,
                45,
                101.5m,
                102.2m,
                101.4m,
                102m,
                20_000m));

            bars.Add(Bar(
                symbol,
                session,
                9,
                46,
                102m,
                108m,
                101.8m,
                107m,
                20_000m));
        }

        return bars;
    }

    private static IntradayBar Bar(
        DateTime session,
        int hour,
        int minute,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume)
        => Bar(
            "SPY",
            session,
            hour,
            minute,
            open,
            high,
            low,
            close,
            volume);

    private static IntradayBar Bar(
        string symbol,
        DateTime session,
        int hour,
        int minute,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume)
    {
        var local = DateTime.SpecifyKind(
            session.Date.AddHours(hour).AddMinutes(minute),
            DateTimeKind.Unspecified);

        var utc = DateTime.SpecifyKind(
            local.AddHours(5),
            DateTimeKind.Utc);

        return new IntradayBar(
            symbol,
            utc,
            local,
            open,
            high,
            low,
            close,
            volume);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.WorkDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Data")) &&
                Directory.Exists(Path.Combine(directory.FullName, "GE360.Trading.Core")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate tradingbot repository root.");
    }
}
