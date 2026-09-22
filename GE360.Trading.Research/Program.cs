using System.Globalization;
using System.Text.Json;

namespace GE360.Trading.Research;

public static class Program
{
    public static int Main(string[] args)
    {
        var root = FindRepositoryRoot();
        var output = GetOption(args, "--output");

        if (args.Contains("--bundled-spy-smoke", StringComparer.OrdinalIgnoreCase))
        {
            return RunBundledSpySmoke(root, output);
        }

        if (args.Contains("--bundled-equity-batch-smoke", StringComparer.OrdinalIgnoreCase))
        {
            return RunBundledEquityBatch(root, output);
        }

        if (args.Contains("--alpaca-download", StringComparer.OrdinalIgnoreCase))
        {
            return RunAlpacaDownloadAsync(root, args, output)
                .GetAwaiter()
                .GetResult();
        }

        var csvDataset = GetOption(args, "--csv-dataset");
        if (!string.IsNullOrWhiteSpace(csvDataset))
        {
            return RunExternalCsvDataset(root, csvDataset, output);
        }

        Console.Error.WriteLine(
            "Usage: dotnet run --project GE360.Trading.Research -- " +
            "(--bundled-spy-smoke | --bundled-equity-batch-smoke | --csv-dataset PATH | --alpaca-download) " +
            "[--symbols SPY,AAPL --start YYYY-MM-DD --end YYYY-MM-DD --feed iex --output-data PATH] " +
            "[--output report.json]");
        return 2;
    }

    private static int RunBundledSpySmoke(string root, string? output)
    {
        var dataDirectory = Path.Combine(
            root,
            "Data",
            "equity",
            "usa",
            "minute",
            "spy");

        var bars = LeanMinuteTradeDataLoader.LoadTradeDirectory(
            dataDirectory,
            "SPY",
            new DateTime(2013, 10, 7),
            new DateTime(2013, 10, 11));

        var sessions = bars
            .Select(x => x.ExchangeLocalTime.Date)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var split = ResearchSplitBuilder.Chronological(sessions, 0.60m);
        var runner = new OpeningRangeResearchRunner();

        var full = runner.Run(bars);
        var inSample = runner.Run(bars.Where(x =>
            split.InSampleSessions.Contains(x.ExchangeLocalTime.Date)));
        var outOfSample = runner.Run(bars.Where(x =>
            split.OutOfSampleSessions.Contains(x.ExchangeLocalTime.Date)));

        var walkForward = WalkForwardPlan.Build(
            sessions,
            trainingSessions: Math.Min(3, Math.Max(1, sessions.Length - 2)),
            testSessions: 1,
            stepSessions: 1);

        var report = new ResearchReport(
            "SPY",
            DateTime.UtcNow,
            "LEAN bundled SPY minute data 2013-10-07..2013-10-11",
            full,
            inSample,
            outOfSample,
            split,
            walkForward,
            "Bundled LEAN data is a smoke dataset only. It is too small for statistical validation or profitability claims.");

        WriteReport(root, output, report);
        return 0;
    }

    private static int RunBundledEquityBatch(string root, string? output)
    {
        var equityMinuteRoot = Path.Combine(
            root,
            "Data",
            "equity",
            "usa",
            "minute");

        var dataset = BundledEquityDataset.Load(equityMinuteRoot);

        if (dataset.CalendarSessions.Count < 2)
        {
            throw new InvalidOperationException(
                "Bundled equity dataset does not contain enough calendar sessions.");
        }

        var split = ResearchSplitBuilder.Chronological(
            dataset.CalendarSessions,
            0.70m);

        var runner = new PortfolioResearchRunner();
        var costs = ResearchCostModel.DeterministicSmokeDefaults;

        var full = runner.Run(
            dataset.Bars,
            costs: costs,
            datasetAdequate: false);

        var inSampleDates = split.InSampleSessions.ToHashSet();
        var outOfSampleDates = split.OutOfSampleSessions.ToHashSet();

        var inSample = runner.Run(
            dataset.Bars.Where(x =>
                inSampleDates.Contains(x.ExchangeLocalTime.Date)),
            costs: costs,
            datasetAdequate: false);

        var outOfSample = runner.Run(
            dataset.Bars.Where(x =>
                outOfSampleDates.Contains(x.ExchangeLocalTime.Date)),
            costs: costs,
            datasetAdequate: false);

        var trainingSessions = Math.Min(
            8,
            Math.Max(1, dataset.CalendarSessions.Count - 2));

        var walkForwardPlan = WalkForwardPlan.Build(
            dataset.CalendarSessions,
            trainingSessions,
            testSessions: 2,
            stepSessions: 2);

        var walkForward = WalkForwardEvaluator.Evaluate(
            runner,
            dataset.Bars,
            walkForwardPlan,
            costs: costs);

        var report = new BundledPortfolioResearchReport(
            DateTime.UtcNow,
            "LEAN bundled US equity minute archives (fragmented engineering dataset)",
            dataset.TradeArchiveCount,
            dataset.Symbols.Count,
            dataset.CalendarSessions.Count,
            dataset.SymbolSessionCount,
            dataset.Symbols,
            dataset.CalendarSessions,
            full,
            inSample,
            outOfSample,
            split,
            walkForward,
            "This bundled dataset is fragmented across symbols and years. " +
            "It is useful for engineering stress tests only and is explicitly " +
            "ineligible to complete phase 7.");

        WriteReport(root, output, report);
        return 0;
    }

    private static async Task<int> RunAlpacaDownloadAsync(
        string root,
        string[] args,
        string? output)
    {
        var symbolsOption = GetRequiredOption(args, "--symbols");
        var startOption = GetRequiredOption(args, "--start");
        var endOption = GetRequiredOption(args, "--end");

        var symbols = symbolsOption
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (symbols.Length == 0)
        {
            throw new ArgumentException("--symbols must contain at least one ticker.");
        }

        var start = ParseUtcBoundary(startOption, inclusiveEnd: false);
        var end = ParseUtcBoundary(endOption, inclusiveEnd: true);
        var feed = GetOption(args, "--feed") ?? "iex";
        var adjustment = GetOption(args, "--adjustment") ?? "all";
        var dataOutput = GetOption(args, "--output-data")
            ?? Path.Combine("artifacts", "ge360-alpaca-minute.csv");

        var resolvedDataOutput = Path.IsPathRooted(dataOutput)
            ? dataOutput
            : Path.GetFullPath(dataOutput, root);

        using var httpClient = new HttpClient();
        var downloader = AlpacaHistoricalDownloader.FromEnvironment(httpClient);

        var result = await downloader.DownloadAsync(
            new AlpacaHistoricalDownloadRequest(
                symbols,
                start,
                end,
                feed,
                adjustment));

        if (result.Bars.Count == 0)
        {
            throw new InvalidOperationException(
                "Alpaca returned zero minute bars for the requested symbols/date range.");
        }

        AlpacaHistoricalDownloader.WriteNeutralCsv(
            resolvedDataOutput,
            result.Bars);

        Console.WriteLine(
            $"GE360 Alpaca download complete: {result.Bars.Count} bars, " +
            $"{result.PageCount} page(s), feed={result.Feed}, file={resolvedDataOutput}");

        return RunExternalCsvDataset(
            root,
            resolvedDataOutput,
            output);
    }

    private static int RunExternalCsvDataset(
        string root,
        string datasetPath,
        string? output)
    {
        var resolvedPath = Path.IsPathRooted(datasetPath)
            ? datasetPath
            : Path.GetFullPath(datasetPath, root);

        var bars = ExternalMinuteCsvLoader.Load(resolvedPath);

        if (bars.Count == 0)
        {
            throw new InvalidOperationException(
                "External CSV dataset does not contain any minute bars.");
        }

        var quality = DatasetQualityAssessment.Assess(bars);

        if (quality.CalendarSessionCount < 2)
        {
            throw new InvalidOperationException(
                "External CSV dataset requires at least two sessions for chronological validation.");
        }

        var sessions = bars
            .Select(x => x.ExchangeLocalTime.Date)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var split = ResearchSplitBuilder.Chronological(
            sessions,
            0.70m);

        var runner = new PortfolioResearchRunner();
        var costs = ResearchCostModel.DeterministicSmokeDefaults;

        var full = runner.Run(
            bars,
            costs: costs,
            datasetAdequate: quality.IsAdequateForPhase7);

        var inSampleDates = split.InSampleSessions.ToHashSet();
        var outOfSampleDates = split.OutOfSampleSessions.ToHashSet();

        var inSample = runner.Run(
            bars.Where(x =>
                inSampleDates.Contains(x.ExchangeLocalTime.Date)),
            costs: costs,
            datasetAdequate: quality.IsAdequateForPhase7);

        var outOfSample = runner.Run(
            bars.Where(x =>
                outOfSampleDates.Contains(x.ExchangeLocalTime.Date)),
            costs: costs,
            datasetAdequate: quality.IsAdequateForPhase7);

        var testSessions = sessions.Length >= 10 ? 5 : 1;
        var trainingSessions = Math.Max(
            1,
            Math.Min(
                sessions.Length - testSessions,
                Math.Max(5, (int)Math.Floor(sessions.Length * 0.60m))));

        var walkForwardPlan = WalkForwardPlan.Build(
            sessions,
            trainingSessions,
            testSessions,
            stepSessions: testSessions);

        var walkForward = WalkForwardEvaluator.Evaluate(
            runner,
            bars,
            walkForwardPlan,
            costs: costs);

        var report = new ExternalPortfolioResearchReport(
            DateTime.UtcNow,
            resolvedPath,
            quality,
            full,
            inSample,
            outOfSample,
            split,
            walkForward,
            quality.IsAdequateForPhase7
                ? "Dataset passed engineering quality checks. OOS/walk-forward/forward-paper results still determine whether phase 7 can advance."
                : "Dataset failed engineering quality checks and cannot complete phase 7.");

        WriteReport(root, output, report);
        return 0;
    }

    private static DateTimeOffset ParseUtcBoundary(
        string value,
        bool inclusiveEnd)
    {
        if (DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            var startOfDay = new DateTimeOffset(
                date.Year,
                date.Month,
                date.Day,
                0,
                0,
                0,
                TimeSpan.Zero);

            return inclusiveEnd
                ? startOfDay.AddDays(1).AddTicks(-1)
                : startOfDay;
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
        {
            throw new ArgumentException(
                $"Unable to parse UTC date/time '{value}'.");
        }

        return timestamp.ToUniversalTime();
    }

    private static void WriteReport<T>(
        string root,
        string? output,
        T report)
    {
        var json = JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions { WriteIndented = true });

        if (output is not null)
        {
            var outputPath = Path.GetFullPath(output, root);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, json);
            Console.WriteLine($"GE360 research report written to {outputPath}");
        }

        Console.WriteLine(json);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Data")) &&
                Directory.Exists(Path.Combine(directory.FullName, "GE360.Trading.Core")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Unable to locate tradingbot repository root.");
    }

    private static string GetRequiredOption(
        string[] args,
        string name)
        => GetOption(args, name)
           ?? throw new ArgumentException(
               $"Required option {name} was not provided.");

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(
                    args[index],
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
