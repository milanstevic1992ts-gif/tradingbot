using GE360.Trading.Observability;
using NUnit.Framework;

namespace GE360.Trading.Tests;

[TestFixture]
public sealed class ObservabilityTests
{
    [Test]
    public void JournalAppendsJsonlAndMaintainsRecentEvents()
    {
        var directory = TempDirectory();

        try
        {
            var coordinator =
                new ObservabilityCoordinator(
                    directory,
                    recentEventLimit: 2);

            var now = new DateTime(
                2026,
                9,
                22,
                13,
                30,
                0,
                DateTimeKind.Utc);

            Assert.That(
                coordinator.TryRecord(Event(now, "ONE")),
                Is.True);

            Assert.That(
                coordinator.TryRecord(
                    Event(
                        now.AddMinutes(1),
                        "TWO")),
                Is.True);

            Assert.That(
                coordinator.TryRecord(
                    Event(
                        now.AddMinutes(2),
                        "THREE")),
                Is.True);

            var store =
                new ObservabilityStore(
                    directory,
                    recentEventLimit: 2);

            var journalPath =
                store.JournalPath(now);

            Assert.That(
                File.Exists(journalPath),
                Is.True);

            Assert.That(
                File.ReadAllLines(journalPath),
                Has.Length.EqualTo(3));

            var recent =
                store.LoadRecentEvents();

            Assert.That(
                recent.Select(x => x.Code),
                Is.EqualTo(
                    new[] { "TWO", "THREE" }));
        }
        finally
        {
            Directory.Delete(
                directory,
                recursive: true);
        }
    }

    [Test]
    public void RuntimeSnapshotRoundTripsAtomically()
    {
        var directory = TempDirectory();

        try
        {
            var store =
                new ObservabilityStore(directory);

            var snapshot =
                new RuntimeObservabilitySnapshot(
                    new DateTime(
                        2026,
                        9,
                        22,
                        14,
                        0,
                        0,
                        DateTimeKind.Utc),
                    "Reducing",
                    99_000m,
                    90_000m,
                    9_000m,
                    -1_000m,
                    0.01m,
                    0.015m,
                    1,
                    0,
                    new[]
                    {
                        new RuntimePositionView(
                            "SPY",
                            18m,
                            500m,
                            500m,
                            9_000m)
                    },
                    true,
                    "risk halt",
                    "Reducing",
                    true,
                    "position mismatch",
                    true,
                    string.Empty,
                    store.JournalPath(
                        new DateTime(
                            2026,
                            9,
                            22,
                            14,
                            0,
                            0,
                            DateTimeKind.Utc)),
                    store.RecentEventsPath);

            store.WriteSnapshot(snapshot);

            var loaded =
                store.LoadSnapshot();

            Assert.That(
                loaded,
                Is.Not.Null);

            Assert.That(
                loaded!.TradingState,
                Is.EqualTo("Reducing"));

            Assert.That(
                loaded.DailyPnl,
                Is.EqualTo(-1_000m));

            Assert.That(
                loaded.Positions,
                Has.Count.EqualTo(1));

            Assert.That(
                File.Exists(
                    store.StatusPath + ".tmp"),
                Is.False);
        }
        finally
        {
            Directory.Delete(
                directory,
                recursive: true);
        }
    }

    [Test]
    public void EventNormalizesSymbolAndUtc()
    {
        var local = DateTime.SpecifyKind(
            new DateTime(
                2026,
                9,
                22,
                15,
                0,
                0),
            DateTimeKind.Local);

        var entry =
            ObservabilityEvent.Create(
                local,
                ObservabilityEventKind.SignalObserved,
                ObservabilitySeverity.Info,
                " SIGNAL ",
                "message",
                " spy ",
                " strategy ");

        Assert.That(
            entry.UtcTime.Kind,
            Is.EqualTo(DateTimeKind.Utc));

        Assert.That(
            entry.Symbol,
            Is.EqualTo("SPY"));

        Assert.That(
            entry.StrategyId,
            Is.EqualTo("strategy"));

        Assert.That(
            entry.Code,
            Is.EqualTo("SIGNAL"));
    }

    private static ObservabilityEvent Event(
        DateTime utc,
        string code)
        => ObservabilityEvent.Create(
            utc,
            ObservabilityEventKind.SignalObserved,
            ObservabilitySeverity.Info,
            code,
            code);

    private static string TempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"ge360-observability-{Guid.NewGuid():N}");

        Directory.CreateDirectory(path);
        return path;
    }
}
