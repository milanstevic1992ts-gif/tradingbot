namespace GE360.Trading.Protection;

public sealed record ProtectionConfig(
    int MaxConsecutiveLossesPerStrategy,
    TimeSpan StrategyCooldownAfterLossStreak,
    TimeSpan SymbolCooldownAfterExit,
    int MaxConsecutiveRiskRejections)
{
    public static ProtectionConfig ConservativePaperDefaults => new(
        MaxConsecutiveLossesPerStrategy: 3,
        StrategyCooldownAfterLossStreak: TimeSpan.FromMinutes(15),
        SymbolCooldownAfterExit: TimeSpan.FromMinutes(2),
        MaxConsecutiveRiskRejections: 10);
}
