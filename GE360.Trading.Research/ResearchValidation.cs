namespace GE360.Trading.Research;

public enum ResearchValidationStatus
{
    SmokeOnly = 0,
    EngineeringSampleOnly = 1,
    MinimumSampleReached = 2
}

public sealed record ResearchValidation(
    ResearchValidationStatus Status,
    int SessionCount,
    int TradeCount,
    int MinimumSessions,
    int MinimumTrades,
    bool DatasetAdequate,
    string Note)
{
    public static ResearchValidation Assess(
        int sessionCount,
        int tradeCount,
        int minimumSessions = 20,
        int minimumTrades = 30,
        bool datasetAdequate = true)
    {
        var countGate = sessionCount >= minimumSessions && tradeCount >= minimumTrades;

        if (!countGate)
        {
            return new ResearchValidation(
                ResearchValidationStatus.SmokeOnly,
                sessionCount,
                tradeCount,
                minimumSessions,
                minimumTrades,
                datasetAdequate,
                "Smoke-test sample only. Do not interpret these results as statistical validation.");
        }

        if (!datasetAdequate)
        {
            return new ResearchValidation(
                ResearchValidationStatus.EngineeringSampleOnly,
                sessionCount,
                tradeCount,
                minimumSessions,
                minimumTrades,
                false,
                "Trade/session count gate reached, but dataset quality is insufficient for phase-7 validation.");
        }

        return new ResearchValidation(
            ResearchValidationStatus.MinimumSampleReached,
            sessionCount,
            tradeCount,
            minimumSessions,
            minimumTrades,
            true,
            "Minimum engineering sample gate reached on an adequate dataset; this is not proof of future profitability.");
    }
}
