namespace GE360.Trading.Recovery;

public sealed record RecoveryConfig(
    decimal QuantityTolerance,
    TimeSpan MaxCheckpointAgeWithExposure)
{
    public static RecoveryConfig ConservativeDefaults => new(
        QuantityTolerance: 0.00000001m,
        MaxCheckpointAgeWithExposure: TimeSpan.FromMinutes(5));

    public void Validate()
    {
        if (QuantityTolerance < 0m ||
            MaxCheckpointAgeWithExposure <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RecoveryConfig),
                "Recovery configuration contains invalid values.");
        }
    }
}
