using System.Globalization;
using System.IO.Compression;
using GE360.Trading.Features;

namespace GE360.Trading.Research;

public static class LeanMinuteTradeDataLoader
{
    private const decimal EquityPriceScale = 10_000m;

    public static IReadOnlyList<IntradayBar> LoadTradeZip(
        string zipPath,
        string symbol)
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("LEAN minute trade archive was not found.", zipPath);
        }

        var fileName = Path.GetFileName(zipPath);
        if (fileName.Length < 8 ||
            !DateTime.TryParseExact(
                fileName[..8],
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var sessionDate))
        {
            throw new FormatException($"Unable to infer session date from '{fileName}'.");
        }

        var eastern = ResolveNewYorkTimeZone();
        var bars = new List<IntradayBar>();

        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(x =>
            x.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            throw new InvalidDataException("LEAN archive does not contain a CSV entry.");
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length < 6)
            {
                throw new InvalidDataException($"Unexpected LEAN minute row: '{line}'.");
            }

            var milliseconds = long.Parse(parts[0], CultureInfo.InvariantCulture);
            var local = DateTime.SpecifyKind(
                sessionDate.Date.AddMilliseconds(milliseconds),
                DateTimeKind.Unspecified);
            var utc = TimeZoneInfo.ConvertTimeToUtc(local, eastern);

            bars.Add(new IntradayBar(
                symbol.Trim().ToUpperInvariant(),
                utc,
                local,
                ParseScaledPrice(parts[1]),
                ParseScaledPrice(parts[2]),
                ParseScaledPrice(parts[3]),
                ParseScaledPrice(parts[4]),
                decimal.Parse(parts[5], CultureInfo.InvariantCulture)));
        }

        return bars;
    }

    public static IReadOnlyList<IntradayBar> LoadTradeDirectory(
        string directory,
        string symbol,
        DateTime? startSession = null,
        DateTime? endSession = null)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(directory);
        }

        var bars = new List<IntradayBar>();

        foreach (var path in Directory
                     .EnumerateFiles(directory, "*_trade.zip")
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (name.Length < 8 ||
                !DateTime.TryParseExact(
                    name[..8],
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var session))
            {
                continue;
            }

            if (startSession.HasValue && session.Date < startSession.Value.Date)
            {
                continue;
            }

            if (endSession.HasValue && session.Date > endSession.Value.Date)
            {
                continue;
            }

            bars.AddRange(LoadTradeZip(path, symbol));
        }

        return bars;
    }

    private static decimal ParseScaledPrice(string value)
        => decimal.Parse(value, CultureInfo.InvariantCulture) / EquityPriceScale;

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
