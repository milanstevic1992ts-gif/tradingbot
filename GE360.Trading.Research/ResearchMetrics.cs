namespace GE360.Trading.Research;

public sealed record ResearchMetrics(
    int TradeCount,
    int WinningTrades,
    int LosingTrades,
    decimal WinRate,
    decimal GrossPnl,
    decimal NetPnl,
    decimal TotalCosts,
    decimal AverageNetPnl,
    decimal MaxDrawdownPercent,
    decimal ReturnPercent,
    decimal? ProfitFactor)
{
    public static ResearchMetrics Calculate(
        IReadOnlyCollection<ResearchTrade> trades,
        decimal initialEquity)
    {
        if (initialEquity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(initialEquity));
        }

        var wins = trades.Where(t => t.NetPnl > 0m).ToArray();
        var losses = trades.Where(t => t.NetPnl < 0m).ToArray();

        var equity = initialEquity;
        var peak = initialEquity;
        var maxDrawdown = 0m;

        foreach (var trade in trades.OrderBy(t => t.ExitTimeUtc))
        {
            equity += trade.NetPnl;
            peak = Math.Max(peak, equity);

            if (peak > 0m)
            {
                maxDrawdown = Math.Max(maxDrawdown, (peak - equity) / peak);
            }
        }

        var gross = trades.Sum(t => t.GrossPnlBeforeCosts);
        var net = trades.Sum(t => t.NetPnl);
        var costs = trades.Sum(t => t.TotalCosts);
        var lossMagnitude = Math.Abs(losses.Sum(t => t.NetPnl));

        return new ResearchMetrics(
            TradeCount: trades.Count,
            WinningTrades: wins.Length,
            LosingTrades: losses.Length,
            WinRate: trades.Count == 0 ? 0m : (decimal)wins.Length / trades.Count,
            GrossPnl: gross,
            NetPnl: net,
            TotalCosts: costs,
            AverageNetPnl: trades.Count == 0 ? 0m : net / trades.Count,
            MaxDrawdownPercent: maxDrawdown,
            ReturnPercent: net / initialEquity,
            ProfitFactor: lossMagnitude > 0m ? wins.Sum(t => t.NetPnl) / lossMagnitude : null);
    }
}
