using GE360.Trading.Features;
using NUnit.Framework;

namespace GE360.Trading.Tests;

[TestFixture]
public sealed class IntradayFeatureEngineTests
{
    private static readonly DateTime SessionDate = new(2026, 9, 22);

    [Test]
    public void BuildsOpeningRangeAndMarksItComplete()
    {
        var engine = NewEngine(atrPeriod: 2, volumeLookback: 2);

        engine.Update(Bar(9, 31, 100m, 102m, 99m, 101m, 1_000m));
        engine.Update(Bar(9, 40, 101m, 103m, 100m, 102m, 1_000m));
        var output = engine.Update(Bar(9, 45, 102m, 104m, 101m, 103m, 2_000m));

        Assert.That(output, Is.Not.Null);
        Assert.That(output!.Features.OpeningRangeComplete, Is.True);
        Assert.That(output.Features.OpeningRangeHigh, Is.EqualTo(103m));
        Assert.That(output.Features.OpeningRangeLow, Is.EqualTo(99m));
    }

    [Test]
    public void ComputesRelativeVolumeFromPriorBars()
    {
        var engine = NewEngine(atrPeriod: 2, volumeLookback: 2);

        engine.Update(Bar(9, 31, 100m, 101m, 99m, 100m, 1_000m));
        engine.Update(Bar(9, 32, 100m, 101m, 99m, 100m, 1_000m));
        var output = engine.Update(Bar(9, 33, 100m, 101m, 99m, 100m, 2_000m));

        Assert.That(output, Is.Not.Null);
        Assert.That(output!.Features.RelativeVolume, Is.EqualTo(2m));
    }

    [Test]
    public void ComputesAtrAfterConfiguredSamples()
    {
        var engine = NewEngine(atrPeriod: 2, volumeLookback: 2);

        var first = engine.Update(Bar(9, 31, 100m, 102m, 99m, 101m, 1_000m));
        var second = engine.Update(Bar(9, 32, 101m, 104m, 100m, 103m, 1_000m));

        Assert.That(first!.Market.Atr, Is.Null);
        Assert.That(second!.Market.Atr, Is.Not.Null);
        Assert.That(second.Market.Atr, Is.GreaterThan(0m));
    }

    [Test]
    public void ResetsSessionStateOnNewExchangeDate()
    {
        var engine = NewEngine(atrPeriod: 2, volumeLookback: 2);

        engine.Update(Bar(9, 31, 100m, 105m, 99m, 104m, 1_000m));

        var nextDay = SessionDate.AddDays(1);
        var output = engine.Update(new IntradayBar(
            "AAPL",
            DateTime.SpecifyKind(nextDay.AddHours(13).AddMinutes(31), DateTimeKind.Utc),
            nextDay.AddHours(9).AddMinutes(31),
            90m,
            91m,
            89m,
            90m,
            500m));

        Assert.That(output, Is.Not.Null);
        Assert.That(output!.Features.OpeningRangeHigh, Is.EqualTo(91m));
        Assert.That(output.Features.OpeningRangeLow, Is.EqualTo(89m));
        Assert.That(output.Features.RelativeVolume, Is.Zero);
        Assert.That(output.Market.Atr, Is.Null);
    }

    private static IntradayFeatureEngine NewEngine(int atrPeriod, int volumeLookback)
        => new(
            "AAPL",
            new IntradayFeatureConfig(
                new TimeSpan(9, 30, 0),
                TimeSpan.FromMinutes(15),
                atrPeriod,
                volumeLookback));

    private static IntradayBar Bar(
        int hour,
        int minute,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume)
    {
        var local = SessionDate.AddHours(hour).AddMinutes(minute);
        var utc = DateTime.SpecifyKind(
            SessionDate.AddHours(hour + 4).AddMinutes(minute),
            DateTimeKind.Utc);

        return new IntradayBar(
            "AAPL",
            utc,
            local,
            open,
            high,
            low,
            close,
            volume);
    }
}
