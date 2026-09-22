namespace GE360.Trading.Features;

public sealed record IntradayFeatureConfig(
    TimeSpan SessionOpenLocalTime,
    TimeSpan OpeningRangeDuration,
    int AtrPeriod,
    int RelativeVolumeLookback)
{
    public static IntradayFeatureConfig UsEquityDefaults => new(
        SessionOpenLocalTime: new TimeSpan(9, 30, 0),
        OpeningRangeDuration: TimeSpan.FromMinutes(15),
        AtrPeriod: 14,
        RelativeVolumeLookback: 20);
}
