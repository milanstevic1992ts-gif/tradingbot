using GE360.Trading.Recovery;
using NUnit.Framework;

namespace GE360.Trading.Tests;

[TestFixture]
public sealed class RecoveryReconciliationTests
{
    private static readonly DateTime Now =
        new(2026, 9, 22, 13, 31, 0, DateTimeKind.Utc);

    [Test]
    public void FreshFlatStartIsSynchronized()
    {
        var result = NewReconciler().Evaluate(
            expected: null,
            Runtime());

        Assert.That(result.IsSynchronized, Is.True);
        Assert.That(result.AllowNewEntries, Is.True);
        Assert.That(result.SymbolsToFlatten, Is.Empty);
    }

    [Test]
    public void ExposureWithoutCheckpointForcesReducing()
    {
        var result = NewReconciler().Evaluate(
            expected: null,
            Runtime(Position("SPY", 10m)));

        Assert.That(result.Mode, Is.EqualTo(RecoveryMode.Reducing));
        Assert.That(result.AllowNewEntries, Is.False);
        Assert.That(result.SymbolsToFlatten, Is.EqualTo(new[] { "SPY" }));
        Assert.That(
            result.Reasons,
            Does.Contain("NO_CHECKPOINT_WITH_RUNTIME_EXPOSURE"));
    }

    [Test]
    public void AnyActualOpenOrderForcesReducingAndCancellation()
    {
        var expected = Checkpoint(
            positions: Array.Empty<RecoveryPositionState>());

        var result = NewReconciler().Evaluate(
            expected,
            Runtime(
                orders: new[] { Order(12, "SPY", 5m) }));

        Assert.That(result.Mode, Is.EqualTo(RecoveryMode.Reducing));
        Assert.That(result.RequiresCancelOpenOrders, Is.True);
        Assert.That(
            result.Reasons,
            Does.Contain("ACTUAL_OPEN_ORDERS_PRESENT"));
    }

    [Test]
    public void MatchingProtectedPositionCanBeRestored()
    {
        var protectedPosition = new RecoveryPositionState(
            "SPY",
            10m,
            500m,
            "opening-range-momentum-v1",
            495m,
            510m);

        var expected = Checkpoint(
            positions: new[] { protectedPosition });

        var result = NewReconciler().Evaluate(
            expected,
            Runtime(Position("SPY", 10m)));

        Assert.That(result.IsSynchronized, Is.True);
        Assert.That(result.ProtectionsToRestore, Has.Count.EqualTo(1));
        Assert.That(
            result.ProtectionsToRestore[0].StopPrice,
            Is.EqualTo(495m));
    }

    [Test]
    public void MatchingPositionWithoutProtectionFailsClosed()
    {
        var expected = Checkpoint(
            positions: new[]
            {
                new RecoveryPositionState(
                    "SPY",
                    10m,
                    500m,
                    null,
                    null,
                    null)
            });

        var result = NewReconciler().Evaluate(
            expected,
            Runtime(Position("SPY", 10m)));

        Assert.That(result.Mode, Is.EqualTo(RecoveryMode.Reducing));
        Assert.That(
            result.Reasons.Any(x =>
                x.StartsWith(
                    "PROTECTION_METADATA_MISSING:",
                    StringComparison.Ordinal)),
            Is.True);
    }

    [Test]
    public void QuantityMismatchForcesReducing()
    {
        var expected = Checkpoint(
            positions: new[]
            {
                new RecoveryPositionState(
                    "SPY",
                    10m,
                    500m,
                    "strategy",
                    495m,
                    510m)
            });

        var result = NewReconciler().Evaluate(
            expected,
            Runtime(Position("SPY", 8m)));

        Assert.That(result.Mode, Is.EqualTo(RecoveryMode.Reducing));
        Assert.That(
            result.Reasons.Any(x =>
                x.StartsWith(
                    "POSITION_MISMATCH:SPY",
                    StringComparison.Ordinal)),
            Is.True);
    }

    [Test]
    public void StaleCheckpointWithExposureForcesReducing()
    {
        var expected = RecoveryCheckpoint.Create(
            Now - TimeSpan.FromMinutes(6),
            gracefulShutdown: false,
            new[]
            {
                new RecoveryPositionState(
                    "SPY",
                    10m,
                    500m,
                    "strategy",
                    495m,
                    510m)
            },
            Array.Empty<RecoveryOpenOrderState>());

        var result = NewReconciler().Evaluate(
            expected,
            Runtime(Position("SPY", 10m)));

        Assert.That(result.Mode, Is.EqualTo(RecoveryMode.Reducing));
        Assert.That(
            result.Reasons,
            Does.Contain("EXPOSED_CHECKPOINT_STALE"));
    }

    [Test]
    public void BrokerAlreadyFlatAfterMismatchRequiresOneBaselineReset()
    {
        var expected = Checkpoint(
            positions: new[]
            {
                new RecoveryPositionState(
                    "SPY",
                    10m,
                    500m,
                    "strategy",
                    495m,
                    510m)
            });

        var result = NewReconciler().Evaluate(
            expected,
            Runtime());

        Assert.That(
            result.Mode,
            Is.EqualTo(RecoveryMode.BaselineResetRequired));
        Assert.That(result.AllowNewEntries, Is.False);
        Assert.That(
            result.Reasons,
            Does.Contain("RUNTIME_FLAT_BASELINE_CAN_BE_RESET"));
    }

    [Test]
    public void CheckpointStoreRoundTripsAtomically()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ge360-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var path = Path.Combine(directory, "checkpoint.json");
            var store = new RecoveryCheckpointStore(path);
            var expected = Checkpoint(
                positions: new[]
                {
                    new RecoveryPositionState(
                        "SPY",
                        10m,
                        500m,
                        "strategy",
                        495m,
                        510m)
                });

            store.Save(expected);
            var loaded = store.Load();

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.Positions["SPY"].Quantity, Is.EqualTo(10m));
            Assert.That(loaded.Positions["SPY"].StopPrice, Is.EqualTo(495m));
            Assert.That(File.Exists(path + ".tmp"), Is.False);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static StartupReconciler NewReconciler()
        => new(new RecoveryConfig(
            QuantityTolerance: 0.000001m,
            MaxCheckpointAgeWithExposure: TimeSpan.FromMinutes(5)));

    private static RecoveryCheckpoint Checkpoint(
        IReadOnlyCollection<RecoveryPositionState> positions)
        => RecoveryCheckpoint.Create(
            Now - TimeSpan.FromSeconds(30),
            gracefulShutdown: false,
            positions,
            Array.Empty<RecoveryOpenOrderState>());

    private static RecoveryRuntimeSnapshot Runtime(
        RecoveryPositionState? position = null,
        IReadOnlyCollection<RecoveryOpenOrderState>? orders = null)
        => RecoveryRuntimeSnapshot.Create(
            Now,
            position is null
                ? Array.Empty<RecoveryPositionState>()
                : new[] { position },
            orders ?? Array.Empty<RecoveryOpenOrderState>());

    private static RecoveryPositionState Position(
        string symbol,
        decimal quantity)
        => RecoveryPositionState.Runtime(
            symbol,
            quantity,
            500m);

    private static RecoveryOpenOrderState Order(
        int id,
        string symbol,
        decimal quantity)
        => new(
            id,
            symbol,
            quantity,
            "Market",
            "Submitted",
            "test",
            Array.Empty<string>());
}
