using GE360.Trading.Features;

namespace GE360.Trading.Research;

public sealed record BundledEquityDataset(
    IReadOnlyList<IntradayBar> Bars,
    IReadOnlyList<string> Symbols,
    IReadOnlyList<DateTime> CalendarSessions,
    int TradeArchiveCount,
    int SymbolSessionCount)
{
    public static BundledEquityDataset Load(string equityMinuteRoot)
    {
        if (!Directory.Exists(equityMinuteRoot))
        {
            throw new DirectoryNotFoundException(equityMinuteRoot);
        }

        var bars = new List<IntradayBar>();
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sessions = new HashSet<DateTime>();
        var archiveCount = 0;
        var symbolSessions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var symbolDirectory in Directory
                     .EnumerateDirectories(equityMinuteRoot)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var tradeArchives = Directory
                .EnumerateFiles(symbolDirectory, "*_trade.zip")
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            if (tradeArchives.Length == 0)
            {
                continue;
            }

            var symbol = Path.GetFileName(symbolDirectory).ToUpperInvariant();
            symbols.Add(symbol);

            foreach (var archive in tradeArchives)
            {
                var loaded = LeanMinuteTradeDataLoader.LoadTradeZip(archive, symbol);
                bars.AddRange(loaded);
                archiveCount++;

                foreach (var session in loaded.Select(x => x.ExchangeLocalTime.Date).Distinct())
                {
                    sessions.Add(session);
                    symbolSessions.Add($"{symbol}:{session:yyyyMMdd}");
                }
            }
        }

        return new BundledEquityDataset(
            bars.OrderBy(x => x.UtcTime).ThenBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase).ToArray(),
            symbols.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            sessions.OrderBy(x => x).ToArray(),
            archiveCount,
            symbolSessions.Count);
    }
}
