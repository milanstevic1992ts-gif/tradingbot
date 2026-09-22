using System.Globalization;
using GE360.Trading.Features;

namespace GE360.Trading.Research;

/// <summary>
/// Provider-neutral minute CSV loader.
/// Required header: timestamp_utc,symbol,open,high,low,close,volume
/// timestamp_utc must include UTC/offset information.
/// </summary>
public static class ExternalMinuteCsvLoader
{
    private static readonly string[] RequiredColumns =
    {
        "timestamp_utc",
        "symbol",
        "open",
        "high",
        "low",
        "close",
        "volume"
    };

    public static IReadOnlyList<IntradayBar> Load(string path)
    {
        if (File.Exists(path))
        {
            return LoadFile(path);
        }

        if (!Directory.Exists(path))
        {
            throw new FileNotFoundException(
                "CSV dataset path does not exist.",
                path);
        }

        var bars = new List<IntradayBar>();

        foreach (var file in Directory
                     .EnumerateFiles(path, "*.csv", SearchOption.AllDirectories)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            bars.AddRange(LoadFile(file));
        }

        return bars
            .OrderBy(x => x.UtcTime)
            .ThenBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<IntradayBar> LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "CSV minute file was not found.",
                path);
        }

        using var reader = new StreamReader(path);
        var headerLine = reader.ReadLine();

        if (string.IsNullOrWhiteSpace(headerLine))
        {
            throw new InvalidDataException("CSV header is missing.");
        }

        var headers = ParseCsvLine(headerLine)
            .Select(x => x.Trim())
            .ToArray();

        var indexes = RequiredColumns.ToDictionary(
            column => column,
            column => Array.FindIndex(
                headers,
                value => string.Equals(
                    value,
                    column,
                    StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

        var missing = indexes
            .Where(pair => pair.Value < 0)
            .Select(pair => pair.Key)
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"CSV is missing required columns: {string.Join(", ", missing)}.");
        }

        var eastern = ResolveNewYorkTimeZone();
        var bars = new List<IntradayBar>();
        var lineNumber = 1;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ParseCsvLine(line);

            if (fields.Count < headers.Length)
            {
                throw new InvalidDataException(
                    $"CSV row {lineNumber} has fewer fields than the header.");
            }

            var timestampText = fields[indexes["timestamp_utc"]].Trim();
            if (!DateTimeOffset.TryParse(
                    timestampText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var timestamp))
            {
                throw new InvalidDataException(
                    $"CSV row {lineNumber} has invalid timestamp_utc '{timestampText}'.");
            }

            var utc = timestamp.UtcDateTime;
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, eastern);

            var bar = new IntradayBar(
                fields[indexes["symbol"]].Trim().ToUpperInvariant(),
                utc,
                DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
                ParseDecimal(fields[indexes["open"]], lineNumber, "open"),
                ParseDecimal(fields[indexes["high"]], lineNumber, "high"),
                ParseDecimal(fields[indexes["low"]], lineNumber, "low"),
                ParseDecimal(fields[indexes["close"]], lineNumber, "close"),
                ParseDecimal(fields[indexes["volume"]], lineNumber, "volume"));

            if (string.IsNullOrWhiteSpace(bar.Symbol) ||
                bar.Open <= 0m ||
                bar.High <= 0m ||
                bar.Low <= 0m ||
                bar.Close <= 0m ||
                bar.High < bar.Low ||
                bar.Volume < 0m)
            {
                throw new InvalidDataException(
                    $"CSV row {lineNumber} contains invalid OHLCV data.");
            }

            bars.Add(bar);
        }

        return bars;
    }

    private static decimal ParseDecimal(
        string value,
        int lineNumber,
        string column)
    {
        if (!decimal.TryParse(
                value.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            throw new InvalidDataException(
                $"CSV row {lineNumber} has invalid {column} value '{value}'.");
        }

        return parsed;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];

            if (character == '"')
            {
                if (quoted &&
                    index + 1 < line.Length &&
                    line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            if (character == ',' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (quoted)
        {
            throw new InvalidDataException("CSV contains an unterminated quoted field.");
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static TimeZoneInfo ResolveNewYorkTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }
}
