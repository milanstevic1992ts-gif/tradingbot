using GE360.Trading.Validation;
using NUnit.Framework;

namespace GE360.Trading.Tests;

[TestFixture]
public sealed class ForwardPaperSessionAccumulatorTests
{
    [Test]
    public void CompletedRoundTripCountsOneClosedTrade()
    {
        var accumulator = NewAccumulator(startedLate: false);

        accumulator.RecordFill(10m);
        accumulator.RecordFill(-10m);

        var session = accumulator.Complete(
            Utc(20, 0),
            endingEquity: 100_025m,
            portfolioFlat: true,
            endedTooEarly: false);

        Assert.That(session.ClosedTrades, Is.EqualTo(1));
        Assert.That(session.NetPnl, Is.EqualTo(25m));
        Assert.That(session.StructuralFailureCount, Is.Zero);
        Assert.That(session.Qualifies, Is.True);
    }

    [Test]
    public void LateStartCannotQualify()
    {
        var accumulator = NewAccumulator(startedLate: true);

        var session = accumulator.Complete(
            Utc(20, 0),
            endingEquity: 100_000m,
            portfolioFlat: true,
            endedTooEarly: false);

        Assert.That(session.StructuralFailureCount, Is.EqualTo(1));
        Assert.That(session.Qualifies, Is.False);
    }

    [Test]
    public void PositionFlipThroughFlatIsStructuralFailure()
    {
        var accumulator = NewAccumulator(startedLate: false);

        accumulator.RecordFill(10m);
        accumulator.RecordFill(-15m);

        var session = accumulator.Complete(
            Utc(20, 0),
            endingEquity: 100_000m,
            portfolioFlat: false,
            endedTooEarly: false);

        Assert.That(session.ClosedTrades, Is.EqualTo(1));
        Assert.That(session.StructuralFailureCount, Is.GreaterThanOrEqualTo(1));
        Assert.That(session.Qualifies, Is.False);
    }

    [Test]
    public void FillLedgerAndPortfolioMustAgreeOnFlatState()
    {
        var accumulator = NewAccumulator(startedLate: false);

        accumulator.RecordFill(10m);

        var session = accumulator.Complete(
            Utc(20, 0),
            endingEquity: 100_000m,
            portfolioFlat: true,
            endedTooEarly: false);

        Assert.That(session.StructuralFailureCount, Is.EqualTo(1));
        Assert.That(session.EndedFlat, Is.False);
        Assert.That(session.Qualifies, Is.False);
    }

    private static ForwardPaperSessionAccumulator NewAccumulator(bool startedLate)
        => new(
            new DateTime(2026, 9, 22),
            Utc(13, 30),
            100_000m,
            startedLate);

    private static DateTime Utc(int hour, int minute)
        => new(
            2026,
            9,
            22,
            hour,
            minute,
            0,
            DateTimeKind.Utc);
}
