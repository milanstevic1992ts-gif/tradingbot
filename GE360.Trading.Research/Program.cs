using System.Text.Json;

namespace GE360.Trading.Research;

public static class Program
{
    public static int Main(string[] args)
    {
        if (!args.Contains("--bundled-spy-smoke", StringComparer.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                "Usage: dotnet run --project GE360.Trading.Research -- --bundled-spy-smoke [--output path]");
            return 2;
        }

        var root = FindRepositoryRoot();
        var dataDirectory = Path.Combine(root, "Data", "equity", "usa", "minute", "spy");

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

        var json = JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions { WriteIndented = true });

        var output = GetOption(args, "--output");
        if (output is not null)
        {
            var outputPath = Path.GetFullPath(output, root);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, json);
            Console.WriteLine($"GE360 research report written to {outputPath}");
        }

        Console.WriteLine(json);
        return 0;
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

        throw new DirectoryNotFoundException("Unable to locate tradingbot repository root.");
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
