using System.Globalization;
using System.Text.Json;
using GE360.Trading.Validation;

namespace GE360.Trading.Research;

public static class Phase7CommandLine
{
    public static bool TryHandle(
        string[] args,
        string repositoryRoot,
        string? output,
        out int exitCode)
    {
        if (args.Contains("--record-paper-session", StringComparer.OrdinalIgnoreCase))
        {
            exitCode = RecordPaperSession(args, repositoryRoot);
            return true;
        }

        if (args.Contains("--paper-summary", StringComparer.OrdinalIgnoreCase))
        {
            exitCode = ShowPaperSummary(args, repositoryRoot, output);
            return true;
        }

        if (args.Contains("--phase7-status", StringComparer.OrdinalIgnoreCase))
        {
            exitCode = ShowPhase7Status(args, repositoryRoot, output);
            return true;
        }

        if (args.Contains("--phase7-gate", StringComparer.OrdinalIgnoreCase))
        {
            exitCode = EvaluateGate(args, repositoryRoot, output);
            return true;
        }

        exitCode = 0;
        return false;
    }

    private static int RecordPaperSession(
        string[] args,
        string repositoryRoot)
    {
        var storePath = ResolvePath(
            repositoryRoot,
            GetRequiredOption(args, "--paper-store"));

        var sessionDate = ParseDate(
            GetRequiredOption(args, "--session-date"));

        var startedAtUtc = ParseUtc(
            GetRequiredOption(args, "--started-utc"));

        var endedAtUtc = ParseUtc(
            GetRequiredOption(args, "--ended-utc"));

        var closedTrades = ParseInt(
            GetRequiredOption(args, "--closed-trades"),
            "--closed-trades");

        var netPnl = ParseDecimal(
            GetRequiredOption(args, "--net-pnl"),
            "--net-pnl");

        var endedFlat = ParseBool(
            GetRequiredOption(args, "--ended-flat"),
            "--ended-flat");

        var structuralFailures = ParseInt(
            GetOption(args, "--structural-failures") ?? "0",
            "--structural-failures");

        var liveAttempts = ParseInt(
            GetOption(args, "--live-attempts") ?? "0",
            "--live-attempts");

        var note = GetOption(args, "--note") ?? string.Empty;

        var store = ForwardPaperObservationStore.Load(storePath);

        store.Add(new ForwardPaperSession(
            sessionDate,
            startedAtUtc,
            endedAtUtc,
            closedTrades,
            netPnl,
            endedFlat,
            structuralFailures,
            liveAttempts,
            note));

        store.Save(storePath);

        var summary = store.Summarize();

        Console.WriteLine(
            $"GE360 forward-paper session recorded: {sessionDate:yyyy-MM-dd}. " +
            $"Qualifying sessions: {summary.QualifyingSessions}/{summary.MinimumRequiredSessions}.");

        return 0;
    }

    private static int ShowPaperSummary(
        string[] args,
        string repositoryRoot,
        string? output)
    {
        var paperStorePath = ResolvePath(
            repositoryRoot,
            GetRequiredOption(args, "--paper-store"));

        var store = ForwardPaperObservationStore.Load(paperStorePath);
        var summary = store.Summarize();
        var json = JsonSerializer.Serialize(summary, JsonOptions());

        if (!string.IsNullOrWhiteSpace(output))
        {
            var outputPath = ResolvePath(repositoryRoot, output);
            var directory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputPath, json);
        }

        Console.WriteLine(json);
        return 0;
    }

    private static int ShowPhase7Status(
        string[] args,
        string repositoryRoot,
        string? output)
    {
        var researchPath = ResolvePath(
            repositoryRoot,
            GetRequiredOption(args, "--research-report"));

        var paperStorePath = ResolvePath(
            repositoryRoot,
            GetRequiredOption(args, "--paper-store"));

        var liveSubmissionEnabled = ParseBool(
            GetOption(args, "--live-submission-enabled") ?? "false",
            "--live-submission-enabled");

        ExternalPortfolioResearchReport? research = null;

        if (File.Exists(researchPath))
        {
            research = JsonSerializer.Deserialize<ExternalPortfolioResearchReport>(
                File.ReadAllText(researchPath),
                JsonOptions())
                ?? throw new InvalidDataException(
                    "Unable to deserialize external portfolio research report.");
        }

        var store = ForwardPaperObservationStore.Load(
            paperStorePath);

        var summary = store.Summarize();

        var progress = Phase7ProgressReport.Build(
            research,
            summary,
            liveSubmissionEnabled);

        var json = JsonSerializer.Serialize(
            progress,
            JsonOptions());

        if (!string.IsNullOrWhiteSpace(output))
        {
            var outputPath = ResolvePath(
                repositoryRoot,
                output);

            var directory =
                Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                outputPath,
                json);
        }

        Console.WriteLine(json);
        return 0;
    }

    private static int EvaluateGate(
        string[] args,
        string repositoryRoot,
        string? output)
    {
        var researchPath = ResolvePath(
            repositoryRoot,
            GetRequiredOption(args, "--research-report"));

        var paperStorePath = ResolvePath(
            repositoryRoot,
            GetRequiredOption(args, "--paper-store"));

        var liveSubmissionEnabled = ParseBool(
            GetRequiredOption(args, "--live-submission-enabled"),
            "--live-submission-enabled");

        if (!File.Exists(researchPath))
        {
            throw new FileNotFoundException(
                "External research report was not found.",
                researchPath);
        }

        var research = JsonSerializer.Deserialize<ExternalPortfolioResearchReport>(
            File.ReadAllText(researchPath),
            JsonOptions())
            ?? throw new InvalidDataException(
                "Unable to deserialize external portfolio research report.");

        var store = ForwardPaperObservationStore.Load(paperStorePath);
        var summary = store.Summarize();

        var result = Phase7GateEvaluator.Evaluate(
            research,
            summary,
            liveSubmissionEnabled);

        var json = JsonSerializer.Serialize(
            result,
            JsonOptions());

        if (!string.IsNullOrWhiteSpace(output))
        {
            var outputPath = ResolvePath(repositoryRoot, output);
            var directory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputPath, json);
        }

        Console.WriteLine(json);

        return result.ReadyForPhase8 ? 0 : 3;
    }

    private static string ResolvePath(
        string root,
        string path)
        => Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, root);

    private static DateTime ParseDate(string value)
    {
        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            throw new ArgumentException(
                $"Invalid session date '{value}'. Expected yyyy-MM-dd.");
        }

        return DateTime.SpecifyKind(
            parsed.Date,
            DateTimeKind.Unspecified);
    }

    private static DateTime ParseUtc(string value)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new ArgumentException(
                $"Invalid UTC date/time '{value}'.");
        }

        return parsed.UtcDateTime;
    }

    private static int ParseInt(
        string value,
        string optionName)
    {
        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed < 0)
        {
            throw new ArgumentException(
                $"{optionName} must be a non-negative integer.");
        }

        return parsed;
    }

    private static decimal ParseDecimal(
        string value,
        string optionName)
    {
        if (!decimal.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            throw new ArgumentException(
                $"{optionName} must be a decimal number.");
        }

        return parsed;
    }

    private static bool ParseBool(
        string value,
        string optionName)
    {
        if (!bool.TryParse(value, out var parsed))
        {
            throw new ArgumentException(
                $"{optionName} must be true or false.");
        }

        return parsed;
    }

    private static string GetRequiredOption(
        string[] args,
        string name)
        => GetOption(args, name)
           ?? throw new ArgumentException(
               $"Required option {name} was not provided.");

    private static string? GetOption(
        string[] args,
        string name)
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

    private static JsonSerializerOptions JsonOptions()
        => new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
}
