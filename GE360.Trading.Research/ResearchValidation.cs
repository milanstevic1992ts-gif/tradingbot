namespace GE360.Trading.Research;

public enum ResearchValidationStatus
{
    SmokeOnly = 0,
    MinimumSampleReached = 1
}

public sealed record ResearchValidation(
    ResearchValidationStatus Status,
    int SessionCount,
    int TradeCount,
    int MinimumSessions,
    int MinimumTrades,
    string Note)
{
    public static ResearchValidation Assess(
        int sessionCount,
        int tradeCount,
        int minimumSessions = 20,
        int minimumTrades = 30)
    {
        var sufficient = sessionCount >= minimumSessions && tradeCount >= minimumTrades;

        return new ResearchValidation(
            sufficient
                ? ResearchValidationStatus.MinimumSampleReached
                : ResearchValidationStatus.SmokeOnly,
            sessionCount,
            tradeCount,
            minimumSessions,
            minimumTrades,
            sufficient
                ? "Minimum engineering sample gate reached; this is not proof of future profitability."
                : "Smoke-test sample only. Do not interpret these results as statistical validation.");
    }
}
