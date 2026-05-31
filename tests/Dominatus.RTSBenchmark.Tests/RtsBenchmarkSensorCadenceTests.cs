using Dominatus.RTSBenchmark.Simulation;

namespace Dominatus.RTSBenchmark.Tests;

public sealed class RtsBenchmarkSensorCadenceTests
{
    [Fact]
    public void RtsBenchmark_SensorCadence_DefaultEnabled()
    {
        var result = SmallRun();

        Assert.True(result.DynamicSensorCadenceEnabled);
        Assert.True(result.SensorRefreshesPerformed > 0);
    }

    [Fact]
    public void RtsBenchmark_SensorCadence_CanDisable()
    {
        var result = SmallRun(new RtsBenchmarkOptions { EnableDynamicSensorCadence = false });

        Assert.False(result.DynamicSensorCadenceEnabled);
        Assert.Equal(0, result.SensorRefreshesSkipped);
        Assert.True(result.SensorRefreshesPerformed > 0);
    }

    [Fact]
    public void RtsBenchmark_SensorCadence_SkipsRefreshesWhenIdleOrDistant()
    {
        var result = SmallRun(new RtsBenchmarkOptions { OverrideShips = 24, OverrideTicks = 80 });

        Assert.True(result.SensorRefreshesSkipped > 0);
        Assert.True(result.StaleTacticalSummaryUses > 0);
    }

    [Fact]
    public void RtsBenchmark_SensorCadence_ImmediateThreatRefreshesEveryTick()
    {
        var simulation = NewCadenceSimulation(shipCount: 2);
        var dominion = simulation.Ships.Single(s => s.Faction == Faction.Dominion);
        var collective = simulation.Ships.Single(s => s.Faction == Faction.Collective);
        dominion.X = 0;
        dominion.Y = 0;
        collective.X = 10;
        collective.Y = 0;

        simulation.RunTicks(6);

        Assert.True(simulation.Metrics.ImmediateCadenceSelections > 0);
        Assert.Equal(0, simulation.Metrics.SensorRefreshesSkipped);
    }

    [Fact]
    public void RtsBenchmark_SensorCadence_DamageForcesRefresh()
    {
        var simulation = NewCadenceSimulation(shipCount: 2);
        foreach (var ship in simulation.Ships)
        {
            ship.X = ship.Faction == Faction.Dominion ? -1_000 : 1_000;
            ship.Y = 0;
        }

        simulation.RunTick(1);
        var damaged = simulation.Ships[0];
        damaged.Hull -= 5;
        simulation.RunTick(2);

        Assert.True(simulation.Metrics.DamageForcedRefreshes > 0);
        Assert.True(simulation.Metrics.SensorRefreshesPerformed > 2);
    }

    [Fact]
    public void RtsBenchmark_SensorCadence_TargetDeathForcesRefresh()
    {
        var simulation = NewCadenceSimulation(shipCount: 2);
        var actor = simulation.Ships.Single(s => s.Faction == Faction.Dominion);
        var target = simulation.Ships.Single(s => s.Faction == Faction.Collective);
        actor.X = -1_000;
        target.X = 1_000;
        simulation.RunTick(1);
        actor.TargetId = target.Id;
        target.Alive = false;
        simulation.RunTick(2);

        Assert.True(simulation.Metrics.TargetInvalidationRefreshes > 0);
    }

    [Fact]
    public void RtsBenchmark_SensorCadence_DisabledModeMatchesNoSkipInvariant()
    {
        var result = SmallRun(new RtsBenchmarkOptions
        {
            EnableDynamicSensorCadence = false,
            OverrideShips = 24,
            OverrideTicks = 40
        });

        Assert.Equal(0, result.SensorRefreshesSkipped);
        Assert.Equal(0, result.StaleTacticalSummaryUses);
        Assert.True(result.SensorRefreshesPerformed > 0);
    }

    [Fact]
    public void RtsBenchmark_SensorCadence_DeterministicHashStable()
    {
        var first = SmallRun();
        var second = SmallRun();

        Assert.Equal(first.DeterminismHash, second.DeterminismHash);
        Assert.Equal(first.SensorRefreshesPerformed, second.SensorRefreshesPerformed);
        Assert.Equal(first.SensorRefreshesSkipped, second.SensorRefreshesSkipped);
        Assert.Equal(first.ImmediateCadenceSelections, second.ImmediateCadenceSelections);
        Assert.Equal(first.NearCadenceSelections, second.NearCadenceSelections);
        Assert.Equal(first.SensorBandCadenceSelections, second.SensorBandCadenceSelections);
        Assert.Equal(first.IdleCadenceSelections, second.IdleCadenceSelections);
    }

    [Fact]
    public void RtsBenchmark_SensorCadence_ReportIncludesDiagnostics()
    {
        using var writer = new StringWriter();

        RtsBenchmarkRunner.Run(new RtsBenchmarkOptions
        {
            WriteCheckpoints = false,
            OverrideShips = 24,
            OverrideTicks = 30
        }, writer);

        var report = writer.ToString();
        Assert.Contains("Sensor cadence diagnostics", report, StringComparison.Ordinal);
        Assert.Contains("Refreshes performed", report, StringComparison.Ordinal);
        Assert.Contains("Refreshes skipped", report, StringComparison.Ordinal);
        Assert.Contains("Skip rate", report, StringComparison.Ordinal);
        Assert.Contains("Average cadence", report, StringComparison.Ordinal);
    }

    [Fact]
    public void RtsBenchmark_SensorCadence_WorksWithBroadScanAndSpatialGrid()
    {
        var broad = SmallRun(new RtsBenchmarkOptions { SensorMode = RtsSensorMode.BroadScan });
        var spatial = SmallRun(new RtsBenchmarkOptions { SensorMode = RtsSensorMode.SpatialGrid });

        Assert.Equal(RtsSensorMode.BroadScan, broad.SensorMode);
        Assert.Equal(RtsSensorMode.SpatialGrid, spatial.SensorMode);
        Assert.True(broad.SensorRefreshesPerformed > 0);
        Assert.True(spatial.SensorRefreshesPerformed > 0);
        Assert.True(broad.SensorRefreshesSkipped > 0);
        Assert.True(spatial.SensorRefreshesSkipped > 0);
    }

    [Fact]
    public void RtsBenchmark_SensorCadence_ValidatesBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SmallRun(new RtsBenchmarkOptions { MinSensorCadenceTicks = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => SmallRun(new RtsBenchmarkOptions { MaxSensorCadenceTicks = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => SmallRun(new RtsBenchmarkOptions { MinSensorCadenceTicks = 4, MaxSensorCadenceTicks = 2 }));
    }

    private static RtsBenchmarkResult SmallRun(RtsBenchmarkOptions? options = null)
    {
        options ??= new RtsBenchmarkOptions();
        options = options with
        {
            WriteCheckpoints = false,
            OverrideShips = options.OverrideShips ?? 24,
            OverrideTicks = options.OverrideTicks ?? 60
        };
        return RtsBenchmarkRunner.Run(options);
    }

    private static BattleSimulation NewCadenceSimulation(int shipCount) => new(
        shipCount,
        checkpointInterval: 500,
        writeCheckpoints: false,
        output: null,
        RtsSensorMode.SpatialGrid,
        RtsBenchmarkRunner.DefaultSpatialCellSize(),
        enableDynamicSensorCadence: true,
        minSensorCadenceTicks: 1,
        maxSensorCadenceTicks: 12);
}
