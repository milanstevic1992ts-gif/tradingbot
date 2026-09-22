namespace GE360.Trading.Execution;

public sealed record ExecutionGuardConfig(
    TimeSpan MaxApprovalAge,
    decimal MaxSlippagePercent,
    decimal MaxVolumeParticipationPercent,
    int MaxSubmissionsPerMinute,
    TimeSpan DuplicateRetention)
{
    public static ExecutionGuardConfig ConservativePaperDefaults => new(
        MaxApprovalAge: TimeSpan.FromSeconds(5),
        MaxSlippagePercent: 0.003m,
        MaxVolumeParticipationPercent: 0.01m,
        MaxSubmissionsPerMinute: 20,
        DuplicateRetention: TimeSpan.FromHours(24));
}
