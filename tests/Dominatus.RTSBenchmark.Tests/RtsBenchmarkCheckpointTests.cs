using Dominatus.RTSBenchmark;
using Dominatus.RTSBenchmark.Simulation;

namespace Dominatus.RTSBenchmark.Tests;

public sealed class RtsBenchmarkCheckpointTests
{
    [Fact]
    public void RtsBenchmark_Checkpoint_StraightVsResumeSameHash()
    {
        var options = SmokeOptions(RtsSensorMode.SpatialGrid);

        var straight = RtsBenchmarkRunner.Run(options);
        var checkpoint = RtsBenchmarkRunner.RunToCheckpoint(options, 100);
        var resumed = RtsBenchmarkRunner.ResumeFromCheckpoint(checkpoint, 150);

        AssertDeterministicEqual(straight, resumed);
    }

    [Fact]
    public void RtsBenchmark_Checkpoint_RoundTripsThroughBytes()
    {
        var checkpoint = RtsBenchmarkRunner.RunToCheckpoint(SmallOptions(), 10);

        var loaded = RtsBenchmarkCheckpointStore.LoadFromBytes(RtsBenchmarkCheckpointStore.SaveToBytes(checkpoint));

        AssertCheckpointShapeEqual(checkpoint, loaded);
    }

    [Fact]
    public void RtsBenchmark_Checkpoint_RoundTripsThroughFile()
    {
        var checkpoint = RtsBenchmarkRunner.RunToCheckpoint(SmallOptions(), 10);
        var path = Path.Combine(Path.GetTempPath(), $"dominatus-rts-test-{Guid.NewGuid():N}.dsave");
        try
        {
            RtsBenchmarkCheckpointStore.SaveToFile(checkpoint, path);
            var loaded = RtsBenchmarkCheckpointStore.LoadFromFile(path);

            AssertCheckpointShapeEqual(checkpoint, loaded);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RtsBenchmark_Checkpoint_RestoresSensorCadenceState()
    {
        var options = SmokeOptions(RtsSensorMode.SpatialGrid) with { EnableDynamicSensorCadence = true };

        var straight = RtsBenchmarkRunner.Run(options);
        var checkpoint = RtsBenchmarkRunner.RunToCheckpoint(options, 100);
        var resumed = RtsBenchmarkRunner.ResumeFromCheckpoint(checkpoint, 150);

        Assert.Contains(checkpoint.Ships, ship => ship.LastTacticalSummary is not null && ship.CurrentSensorCadenceTicks > 1);
        Assert.Equal(straight.DeterminismHash, resumed.DeterminismHash);
        Assert.Equal(straight.SensorRefreshesPerformed, resumed.SensorRefreshesPerformed);
        Assert.Equal(straight.SensorRefreshesSkipped, resumed.SensorRefreshesSkipped);
        Assert.Equal(straight.ImmediateCadenceSelections, resumed.ImmediateCadenceSelections);
        Assert.Equal(straight.NearCadenceSelections, resumed.NearCadenceSelections);
        Assert.Equal(straight.SensorBandCadenceSelections, resumed.SensorBandCadenceSelections);
        Assert.Equal(straight.IdleCadenceSelections, resumed.IdleCadenceSelections);
    }

    [Fact]
    public void RtsBenchmark_Checkpoint_WorksWithSpatialGrid()
    {
        var options = SmokeOptions(RtsSensorMode.SpatialGrid);

        var straight = RtsBenchmarkRunner.Run(options);
        var checkpoint = RtsBenchmarkRunner.RunToCheckpoint(options, 100);
        var resumed = RtsBenchmarkRunner.ResumeFromCheckpoint(checkpoint, 150);

        Assert.Equal(straight.DeterminismHash, resumed.DeterminismHash);
        Assert.Equal(straight.SpatialCandidatePairs, resumed.SpatialCandidatePairs);
    }

    [Fact]
    public void RtsBenchmark_Checkpoint_WorksWithBroadScan()
    {
        var options = SmokeOptions(RtsSensorMode.BroadScan);

        var straight = RtsBenchmarkRunner.Run(options);
        var checkpoint = RtsBenchmarkRunner.RunToCheckpoint(options, 100);
        var resumed = RtsBenchmarkRunner.ResumeFromCheckpoint(checkpoint, 150);

        Assert.Equal(straight.DeterminismHash, resumed.DeterminismHash);
        Assert.Equal(straight.BroadSensorPairsEquivalent, resumed.BroadSensorPairsEquivalent);
    }

    [Fact]
    public void RtsBenchmark_Checkpoint_DoesNotCompareTimingAllocation()
    {
        var options = SmokeOptions(RtsSensorMode.SpatialGrid);

        var straight = RtsBenchmarkRunner.Run(options);
        var checkpoint = RtsBenchmarkRunner.RunToCheckpoint(options, 100);
        var resumed = RtsBenchmarkRunner.ResumeFromCheckpoint(checkpoint, 150);

        AssertDeterministicEqual(straight, resumed);
        Assert.NotNull(straight.PhaseTimings);
        Assert.NotNull(resumed.PhaseTimings);
    }

    [Fact]
    public void RtsBenchmark_Checkpoint_RejectsUnsupportedVersion()
    {
        var checkpoint = RtsBenchmarkRunner.RunToCheckpoint(SmallOptions(), 10) with { Version = RtsBenchmarkCheckpoint.CurrentVersion + 1 };

        var ex = Assert.Throws<InvalidDataException>(() => RtsBenchmarkCheckpointStore.LoadFromBytes(RtsBenchmarkCheckpointStore.SaveToBytes(checkpoint)));
        Assert.Contains("Unsupported RTSBenchmark checkpoint version", ex.Message);
    }

    [Fact]
    public void RtsBenchmark_Checkpoint_RejectsDuplicateShipIds()
    {
        var checkpoint = RtsBenchmarkRunner.RunToCheckpoint(SmallOptions(), 10);
        var ships = checkpoint.Ships.ToArray();
        ships[1] = ships[1] with { Id = ships[0].Id };
        var duplicate = checkpoint with { Ships = ships };

        var ex = Assert.Throws<InvalidDataException>(() => RtsBenchmarkCheckpointStore.LoadFromBytes(RtsBenchmarkCheckpointStore.SaveToBytes(duplicate)));
        Assert.Contains("Duplicate ship id", ex.Message);
    }

    private static RtsBenchmarkOptions SmokeOptions(RtsSensorMode sensorMode) => new()
    {
        OverrideShips = 50,
        OverrideTicks = 250,
        WriteCheckpoints = false,
        SensorMode = sensorMode,
        EnableDynamicSensorCadence = true
    };

    private static RtsBenchmarkOptions SmallOptions() => new()
    {
        OverrideShips = 20,
        OverrideTicks = 25,
        WriteCheckpoints = false
    };

    private static void AssertCheckpointShapeEqual(RtsBenchmarkCheckpoint expected, RtsBenchmarkCheckpoint actual)
    {
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.CompletedTicks, actual.CompletedTicks);
        Assert.Equal(expected.Ships.Count, actual.Ships.Count);
        Assert.Equal(expected.Metrics.Counters["AgentTicks"], actual.Metrics.Counters["AgentTicks"]);
        Assert.Equal(expected.Diagnostics.SensorMode, actual.Diagnostics.SensorMode);
        Assert.Equal(expected.Ships.Select(s => s.Id), actual.Ships.Select(s => s.Id));
    }

    private static void AssertDeterministicEqual(RtsBenchmarkResult expected, RtsBenchmarkResult actual)
    {
        Assert.Equal(expected.DeterminismHash, actual.DeterminismHash);
        Assert.Equal(expected.AgentTicks, actual.AgentTicks);
        Assert.Equal(expected.DecisionsEvaluated, actual.DecisionsEvaluated);
        Assert.Equal(expected.ActionsEmitted, actual.ActionsEmitted);
        Assert.Equal(expected.EventsDelivered, actual.EventsDelivered);
        Assert.Equal(expected.DamageEvents, actual.DamageEvents);
        Assert.Equal(expected.RepairEvents, actual.RepairEvents);
        Assert.Equal(expected.DestroyedShips, actual.DestroyedShips);
        Assert.Equal(expected.Winner, actual.Winner);
        Assert.Equal(expected.FinalShips, actual.FinalShips);
        Assert.Equal(expected.DominionFleetPower, actual.DominionFleetPower);
        Assert.Equal(expected.CollectiveFleetPower, actual.CollectiveFleetPower);
        Assert.Equal(expected.DominionActions, actual.DominionActions);
        Assert.Equal(expected.CollectiveActions, actual.CollectiveActions);
        Assert.Equal(expected.DominionEvents, actual.DominionEvents);
        Assert.Equal(expected.CollectiveEvents, actual.CollectiveEvents);
    }
}
