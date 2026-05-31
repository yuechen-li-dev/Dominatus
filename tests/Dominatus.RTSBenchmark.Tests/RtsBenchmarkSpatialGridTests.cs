using Dominatus.RTSBenchmark;
using Dominatus.RTSBenchmark.Simulation;

namespace Dominatus.RTSBenchmark.Tests;

public sealed class RtsBenchmarkSpatialGridTests
{
    [Fact]
    public void RtsBenchmark_SpatialGrid_DefaultModeIsSpatialGrid()
    {
        var options = new RtsBenchmarkOptions();

        Assert.Equal(RtsSensorMode.SpatialGrid, options.SensorMode);
    }

    [Fact]
    public void RtsBenchmark_BroadScanMode_RemainsAvailable()
    {
        var result = RtsBenchmarkRunner.Run(new RtsBenchmarkOptions
        {
            SensorMode = RtsSensorMode.BroadScan,
            OverrideShips = 20,
            OverrideTicks = 5,
            WriteCheckpoints = false
        });

        Assert.Equal(RtsSensorMode.BroadScan, result.SensorMode);
        Assert.Equal(result.BroadSensorPairsEquivalent, result.SensorPairsChecked);
    }

    [Fact]
    public void RtsBenchmark_SpatialGrid_DeterministicHashStable()
    {
        var a = SmallSpatialRun();
        var b = SmallSpatialRun();

        Assert.Equal(a.DeterminismHash, b.DeterminismHash);
        Assert.Equal(a.SensorPairsChecked, b.SensorPairsChecked);
        Assert.Equal(a.SpatialCandidatePairs, b.SpatialCandidatePairs);
        Assert.Equal(a.BroadSensorPairsEquivalent, b.BroadSensorPairsEquivalent);
    }

    [Fact]
    public void RtsBenchmark_SpatialGrid_ReducesCandidatePairsInSpreadScenario()
    {
        var result = RtsBenchmarkRunner.Run(new RtsBenchmarkOptions
        {
            SensorMode = RtsSensorMode.SpatialGrid,
            OverrideShips = 80,
            OverrideTicks = 5,
            WriteCheckpoints = false
        });

        Assert.True(result.SpatialCandidatePairs < result.BroadSensorPairsEquivalent,
            $"Expected spatial candidates {result.SpatialCandidatePairs} to be below broad equivalent {result.BroadSensorPairsEquivalent}.");
        Assert.True(result.SpatialPairsSkippedByGrid > 0);
    }

    [Fact]
    public void RtsBenchmark_SpatialGrid_DoesNotMissNearbyEnemy()
    {
        var ship = NewShip(1, Faction.Dominion, ShipClass.MissileCorvette, x: 0, y: 0);
        var enemy = NewShip(2, Faction.Collective, ShipClass.NeedleDrone, x: 20, y: 0);
        var grid = BuildGrid(72f, [enemy, ship]);
        var candidates = QueryShips(grid, ship, ShipClassDefinition.Get(ship.Class).SensorRange);
        var counters = new TacticalContactCounters();

        var summary = TacticalModel.ComputeSummary(ship, candidates, -1, ref counters);

        Assert.Equal(enemy.Id, summary.BestAttackTargetId);
        Assert.True(counters.RelevantEnemyContacts > 0);
    }

    [Fact]
    public void RtsBenchmark_SpatialGrid_IgnoresFarEnemy()
    {
        var ship = NewShip(1, Faction.Dominion, ShipClass.MissileCorvette, x: 0, y: 0);
        var farEnemy = NewShip(2, Faction.Collective, ShipClass.NeedleDrone, x: 500, y: 0);
        var grid = BuildGrid(72f, [farEnemy, ship]);
        var candidates = QueryShips(grid, ship, ShipClassDefinition.Get(ship.Class).SensorRange);
        var counters = new TacticalContactCounters();

        var summary = TacticalModel.ComputeSummary(ship, candidates, -1, ref counters);

        Assert.Null(summary.BestAttackTargetId);
        Assert.DoesNotContain(candidates, candidate => candidate.Id == farEnemy.Id);
    }

    [Fact]
    public void RtsBenchmark_SpatialGrid_NegativeCoordinatesSupported()
    {
        var ship = NewShip(1, Faction.Dominion, ShipClass.ScoutFrigate, x: -73, y: -73);
        var enemy = NewShip(2, Faction.Collective, ShipClass.NeedleDrone, x: -70, y: -72);
        var grid = BuildGrid(72f, [enemy, ship]);

        var candidates = QueryShips(grid, ship, ShipClassDefinition.Get(ship.Class).SensorRange);

        Assert.Contains(candidates, candidate => candidate.Id == enemy.Id);
    }

    [Fact]
    public void RtsBenchmark_SpatialGrid_CandidateOrderIsDeterministic()
    {
        var ship = NewShip(10, Faction.Dominion, ShipClass.ScoutFrigate, x: 0, y: 0);
        var three = NewShip(3, Faction.Collective, ShipClass.NeedleDrone, x: 5, y: 0);
        var one = NewShip(1, Faction.Collective, ShipClass.NeedleDrone, x: 6, y: 0);
        var two = NewShip(2, Faction.Collective, ShipClass.NeedleDrone, x: -5, y: 0);
        var grid = BuildGrid(72f, [ship, three, one, two]);

        var candidateIds = grid.QueryCandidateIds(ship.X, ship.Y, 72f, out _).Where(id => id != ship.Id).ToArray();

        Assert.Equal([1, 2, 3], candidateIds);
    }

    [Fact]
    public void RtsBenchmark_ReportIncludesSpatialDiagnostics()
    {
        using var output = new StringWriter();

        RtsBenchmarkRunner.Run(new RtsBenchmarkOptions
        {
            OverrideShips = 20,
            OverrideTicks = 5,
            WriteCheckpoints = false
        }, output);

        var report = output.ToString();
        Assert.Contains("Sensor mode", report);
        Assert.Contains("Spatial candidate pairs", report);
        Assert.Contains("Pairs skipped by grid", report);
    }

    private static RtsBenchmarkResult SmallSpatialRun() => RtsBenchmarkRunner.Run(new RtsBenchmarkOptions
    {
        SensorMode = RtsSensorMode.SpatialGrid,
        OverrideShips = 20,
        OverrideTicks = 10,
        WriteCheckpoints = false
    });

    private static SpatialShipGrid BuildGrid(float cellSize, IReadOnlyList<ShipState> ships)
    {
        var grid = new SpatialShipGrid(cellSize, ships);
        grid.Rebuild(ships);
        return grid;
    }

    private static IReadOnlyList<ShipState> QueryShips(SpatialShipGrid grid, ShipState ship, float range) =>
        grid.QueryCandidateIds(ship.X, ship.Y, range, out _)
            .Where(id => id != ship.Id)
            .Select(grid.ShipById)
            .ToArray();

    private static ShipState NewShip(int id, Faction faction, ShipClass shipClass, float hullFraction = 1f, float x = 0f, float y = 0f)
    {
        var def = ShipClassDefinition.Get(shipClass);
        return new ShipState
        {
            Id = id,
            Faction = faction,
            Class = shipClass,
            X = x,
            Y = y,
            Hull = def.Hull * hullFraction,
            ShieldOrCarapace = def.ShieldOrCarapace,
            CooldownRemaining = 0
        };
    }
}
