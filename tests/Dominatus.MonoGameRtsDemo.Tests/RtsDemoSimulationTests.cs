using Dominatus.MonoGameConn;
using Dominatus.MonoGameRtsDemo;
using Microsoft.Xna.Framework;

namespace Dominatus.MonoGameRtsDemo.Tests;

public sealed class RtsDemoSimulationTests
{
    [Fact]
    public void DemoSimulation_CreatesExpectedAgentCount()
    {
        var simulation = RtsDemoSimulation.Create();

        Assert.Equal(RtsDemoOptions.DefaultShips, simulation.Ships.Count);
        Assert.Equal(RtsDemoOptions.DefaultShips, simulation.World.Agents.Count);
        Assert.Equal(22, simulation.DominionAlive);
        Assert.Equal(28, simulation.CollectiveAlive);
    }


    [Fact]
    public void DemoSimulation_CollectiveStartsWithNumericalAdvantage()
    {
        var simulation = RtsDemoSimulation.Create();

        Assert.Equal(RtsDemoOptions.DefaultShips, simulation.DominionAlive + simulation.CollectiveAlive);
        Assert.True(simulation.CollectiveAlive > simulation.DominionAlive, $"Expected the Collective swarm to outnumber Dominion, but counts were D:{simulation.DominionAlive} C:{simulation.CollectiveAlive}.");
    }

    [Fact]
    public void DemoSimulation_VelocityChangesSmoothly()
    {
        var current = new Vector2(120f, 0f);
        var desired = new Vector2(-120f, 0f);

        var smoothed = RtsDemoSimulation.SmoothVelocity(current, desired, responsiveness: 5f, dt: 0.1f);

        Assert.True(smoothed.X > desired.X, $"Expected smoothing to avoid an instant snap to {desired}, but got {smoothed}.");
        Assert.True(smoothed.X < current.X, $"Expected smoothing to move toward {desired}, but got {smoothed}.");
    }

    [Fact]
    public void DemoSimulation_AlliedMinimumSpacingResolverSeparatesOverlaps()
    {
        var simulation = RtsDemoSimulation.Create();
        var allies = simulation.Ships.Where(s => s.Faction == RtsFaction.Collective).Take(2).ToArray();
        var overlap = new Vector2(1000f, 540f);
        allies[0].Position = overlap;
        allies[1].Position = overlap;

        simulation.Step(0.1f);

        var distance = Vector2.Distance(allies[0].Position, allies[1].Position);
        Assert.True(distance > 0f, $"Expected deterministic allied spacing to separate overlapping ships, but distance was {distance}.");
        Assert.All(allies, ship =>
        {
            Assert.InRange(ship.Position.X, 20f, RtsDemoSimulation.WorldWidth - 20f);
            Assert.InRange(ship.Position.Y, 20f, RtsDemoSimulation.WorldHeight - 20f);
        });
    }

    [Fact]
    public void DemoSimulation_ActionFlappingIsBounded()
    {
        var simulation = RtsDemoSimulation.Create();
        var previous = simulation.Ships.ToDictionary(s => s.AgentId, s => s.Agent.Bb.GetOrDefault(RtsDemoKeys.CurrentAction, ""));
        var changes = simulation.Ships.ToDictionary(s => s.AgentId, _ => 0);

        for (var i = 0; i < 160; i++)
        {
            simulation.Step(0.1f);
            foreach (var ship in simulation.Ships.Where(s => s.Alive))
            {
                var action = ship.Agent.Bb.GetOrDefault(RtsDemoKeys.CurrentAction, "");
                if (action != previous[ship.AgentId])
                {
                    changes[ship.AgentId]++;
                    previous[ship.AgentId] = action;
                }
            }
        }

        var maxChanges = changes.Values.Max();
        Assert.True(maxChanges <= 24, $"Expected visual action commitment to keep flapping bounded, but one ship changed action {maxChanges} times.");
    }

    [Fact]
    public void DemoSimulation_CreatesClassDiverseFleets()
    {
        var simulation = RtsDemoSimulation.Create();

        Assert.True(simulation.Ships.Where(s => s.Faction == RtsFaction.Dominion).Select(s => s.Class).Distinct().Count() > 1);
        Assert.True(simulation.Ships.Where(s => s.Faction == RtsFaction.Collective).Select(s => s.Class).Distinct().Count() > 1);
    }

    [Fact]
    public void DemoSimulation_HomePositionsAreAssigned()
    {
        var simulation = RtsDemoSimulation.Create();
        var initialHomes = HomeSnapshot(simulation);

        Assert.All(simulation.Ships, ship =>
        {
            Assert.True(ship.HomePosition.X > 0f);
            Assert.True(ship.HomePosition.Y > 0f);
            Assert.Equal(ship.HomePosition, ship.Position);
        });

        simulation.Step(0.25f);
        simulation.Reset();

        Assert.Equal(initialHomes, HomeSnapshot(simulation));
    }

    [Fact]
    public void DemoSimulation_FormationDriftUsesHomeY()
    {
        var simulation = RtsDemoSimulation.Create(2);
        var ship = simulation.Ships.First(s => s.Faction == RtsFaction.Dominion);
        ship.Position = new Vector2(ship.HomePosition.X, ship.HomePosition.Y + 80f);

        var drift = RtsDemoSimulation.CalculateFormationDrift(ship);

        Assert.True(drift.Y < 0f, $"Expected formation drift to move upward toward home Y {ship.HomePosition.Y}, but drift was {drift}.");
    }

    [Fact]
    public void DemoSimulation_ShipsDoNotClumpIntoSingleStack()
    {
        var simulation = RtsDemoSimulation.Create();

        for (var i = 0; i < 160; i++)
            simulation.Step(0.1f);

        var nearIdenticalPairs = CountSameFactionPairsWithin(simulation, 4f);

        Assert.True(nearIdenticalPairs < 12, $"Expected allied separation to prevent stack clumping, but found {nearIdenticalPairs} same-faction pairs within 4px.");
    }

    [Fact]
    public void DemoSimulation_LaserFlashOccursWhenShipsFire()
    {
        var simulation = RtsDemoSimulation.Create();

        var fired = RunUntil(simulation, 35f, 0.1f, () =>
            simulation.Ships.Any(ship => ship.FiredThisFrame && ship.LaserTargetPos is not null));

        Assert.True(fired, "At least one ship should record a one-frame laser flash when a shot lands.");
    }

    [Fact]
    public void DemoSimulation_StillClosesIntoCombatAndDamagesShips()
    {
        var simulation = RtsDemoSimulation.Create();
        var initialHull = simulation.Ships.Sum(s => s.Hull);

        var damaged = RunUntil(simulation, 45f, 0.1f, () => simulation.Ships.Sum(s => s.Hull) < initialHull);

        Assert.True(damaged);
    }

    [Fact]
    public void DemoSimulation_TargetsRespectSensorRange()
    {
        var simulation = RtsDemoSimulation.Create(2);
        var dominion = simulation.Ships.Single(s => s.Faction == RtsFaction.Dominion);
        var collective = simulation.Ships.Single(s => s.Faction == RtsFaction.Collective);
        dominion.Position = new Vector2(100f, 100f);
        collective.Position = new Vector2(1800f, 900f);

        simulation.UpdatePerception();

        Assert.Null(dominion.TargetId);
        Assert.Null(collective.TargetId);
        Assert.False(dominion.Agent.Bb.GetOrDefault(RtsDemoKeys.EnemyInRange, true));
        Assert.False(collective.Agent.Bb.GetOrDefault(RtsDemoKeys.EnemyInRange, true));
        Assert.False(dominion.Agent.Bb.TryGet(RtsDemoKeys.TargetId, out _));
        Assert.False(collective.Agent.Bb.TryGet(RtsDemoKeys.TargetId, out _));
    }

    [Fact]
    public void RtsDemoSpatialGrid_FindsNearbyAndIgnoresFar()
    {
        var simulation = RtsDemoSimulation.Create(3);
        var ships = simulation.Ships.ToArray();
        ships[0].Position = new Vector2(100f, 100f);
        ships[1].Position = new Vector2(130f, 100f);
        ships[2].Position = new Vector2(900f, 900f);
        var grid = new RtsDemoSpatialGrid(100f);

        grid.Rebuild(ships);
        var candidates = grid.QueryCandidateIds(ships[0].Position, 100f);

        Assert.Contains(ships[0].AgentId, candidates);
        Assert.Contains(ships[1].AgentId, candidates);
        Assert.DoesNotContain(ships[2].AgentId, candidates);
    }

    [Fact]
    public void RtsDemoSpatialGrid_DeterministicCandidateOrder()
    {
        var simulation = RtsDemoSimulation.Create(6);
        foreach (var ship in simulation.Ships)
            ship.Position = new Vector2(200f + ship.Index % 2, 200f + ship.Index % 3);
        var grid = new RtsDemoSpatialGrid(100f);

        grid.Rebuild(simulation.Ships.Reverse());
        var first = grid.QueryCandidateIds(new Vector2(200f, 200f), 100f).Select(id => id.Value).ToArray();
        grid.Rebuild(simulation.Ships);
        var second = grid.QueryCandidateIds(new Vector2(200f, 200f), 100f).Select(id => id.Value).ToArray();

        Assert.Equal(first.Order().ToArray(), first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void DemoSimulation_UsesMonoGameBbPositionKeys()
    {
        var simulation = RtsDemoSimulation.Create();

        foreach (var ship in simulation.Ships)
        {
            Assert.True(MonoGameBbKeys.TryGetPosition(ship.Agent, out var position));
            Assert.Equal(ship.Position, position);
        }
    }

    [Fact]
    public void DemoSimulation_AgentDecisionProducesActions()
    {
        var simulation = RtsDemoSimulation.Create();

        simulation.Step(0.016f);
        simulation.Step(0.016f);

        Assert.All(simulation.Ships, ship =>
        {
            Assert.True(ship.Agent.Bb.GetOrDefault(RtsDemoKeys.UsedAiDecide, false));
            Assert.Contains(ship.Agent.Bb.GetOrDefault(RtsDemoKeys.CurrentAction, ""), new[] { "Advance", "Attack", "Retreat", "HoldFormation" });
        });
    }

    [Fact]
    public void AttackScore_OutOfRange_IsZeroOrCannotBeatAdvance()
    {
        var simulation = RtsDemoSimulation.Create(2);
        var dominion = simulation.Ships.Single(s => s.Faction == RtsFaction.Dominion);
        var collective = simulation.Ships.Single(s => s.Faction == RtsFaction.Collective);

        dominion.Position = new Vector2(500f, 500f);
        collective.Position = new Vector2(700f, 500f);
        dominion.Cooldown = 0f;
        collective.Cooldown = 0f;

        simulation.Step(0.1f);
        simulation.Step(0.1f);

        Assert.False(dominion.Agent.Bb.GetOrDefault(RtsDemoKeys.EnemyInRange, true));
        Assert.False(collective.Agent.Bb.GetOrDefault(RtsDemoKeys.EnemyInRange, true));
        Assert.NotEqual("Attack", dominion.Agent.Bb.GetOrDefault(RtsDemoKeys.CurrentAction, ""));
        Assert.NotEqual("Attack", collective.Agent.Bb.GetOrDefault(RtsDemoKeys.CurrentAction, ""));
    }

    [Fact]
    public void DemoSimulation_ShipsCloseIntoAttackRange()
    {
        var simulation = RtsDemoSimulation.Create();

        var reachedAttackWhileInRange = RunUntil(simulation, 30f, 0.1f, () =>
            simulation.Ships.Any(ship =>
                ship.Alive
                && ship.Agent.Bb.GetOrDefault(RtsDemoKeys.CurrentAction, "") == "Attack"
                && ship.Agent.Bb.GetOrDefault(RtsDemoKeys.EnemyInRange, false)));

        Assert.True(reachedAttackWhileInRange);
    }

    [Fact]
    public void DemoSimulation_CombatEventuallyDestroysOrDamagesShips()
    {
        var simulation = RtsDemoSimulation.Create();
        var initialHull = simulation.Ships.Sum(s => s.Hull);
        var initialAlive = simulation.DominionAlive + simulation.CollectiveAlive;

        var combatResolved = RunUntil(simulation, 45f, 0.1f, () =>
            simulation.Ships.Sum(s => s.Hull) < initialHull
            || simulation.DominionAlive + simulation.CollectiveAlive < initialAlive);

        Assert.True(combatResolved);
    }

    [Fact]
    public void DemoSimulation_DamagedShipDoesNotRetreatForeverWhenEnemyFar()
    {
        var simulation = RtsDemoSimulation.Create(2);
        var dominion = simulation.Ships.Single(s => s.Faction == RtsFaction.Dominion);
        var collective = simulation.Ships.Single(s => s.Faction == RtsFaction.Collective);

        dominion.Position = new Vector2(200f, 300f);
        collective.Position = new Vector2(1500f, 300f);
        collective.Hull = 20f;

        var actions = RunActions(simulation, collective, 2f, 0.1f);

        Assert.Contains(actions, action => action is "Advance" or "HoldFormation");
        Assert.NotEqual("Retreat", actions[^1]);
    }

    [Fact]
    public void DemoSimulation_RetreatDoesNotDriveCollectiveIntoCornerForever()
    {
        var simulation = RtsDemoSimulation.Create(2);
        var dominion = simulation.Ships.Single(s => s.Faction == RtsFaction.Dominion);
        var collective = simulation.Ships.Single(s => s.Faction == RtsFaction.Collective);

        dominion.Position = new Vector2(RtsDemoSimulation.WorldWidth - 150f, RtsDemoSimulation.WorldHeight - 120f);
        collective.Position = new Vector2(RtsDemoSimulation.WorldWidth - 25f, RtsDemoSimulation.WorldHeight - 25f);
        collective.Hull = 25f;

        var consecutiveCornerRetreatTicks = 0;
        var maxConsecutiveCornerRetreatTicks = 0;
        for (var i = 0; i < 80; i++)
        {
            simulation.Step(0.1f);
            var action = collective.Agent.Bb.GetOrDefault(RtsDemoKeys.CurrentAction, "");
            var nearLowerRightCorner = collective.Position.X >= RtsDemoSimulation.WorldWidth - 35f
                && collective.Position.Y >= RtsDemoSimulation.WorldHeight - 35f;

            if (collective.Alive && action == "Retreat" && nearLowerRightCorner)
                consecutiveCornerRetreatTicks++;
            else
                consecutiveCornerRetreatTicks = 0;

            maxConsecutiveCornerRetreatTicks = Math.Max(maxConsecutiveCornerRetreatTicks, consecutiveCornerRetreatTicks);
        }

        Assert.True(maxConsecutiveCornerRetreatTicks < 8, $"Collective ship stayed clamped in lower-right retreat for {maxConsecutiveCornerRetreatTicks} consecutive ticks at {collective.Position}.");
        Assert.True(collective.Position.X < RtsDemoSimulation.WorldWidth - 35f || collective.Position.Y < RtsDemoSimulation.WorldHeight - 35f);
    }

    [Fact]
    public void DemoSimulation_DamagedShipsCanRecoverAndReengage()
    {
        var simulation = RtsDemoSimulation.Create(2);
        var dominion = simulation.Ships.Single(s => s.Faction == RtsFaction.Dominion);
        var collective = simulation.Ships.Single(s => s.Faction == RtsFaction.Collective);

        dominion.Position = new Vector2(790f, 520f);
        collective.Position = new Vector2(960f, 520f);
        collective.Hull = 35f;
        collective.Cooldown = 2f;
        dominion.Cooldown = 10f;

        var retreated = RunUntil(simulation, 1f, 0.1f, () =>
            collective.Agent.Bb.GetOrDefault(RtsDemoKeys.CurrentAction, "") == "Retreat");

        dominion.Position = new Vector2(260f, 520f);
        var recovered = RunUntil(simulation, 3f, 0.1f, () =>
            collective.Alive && collective.Agent.Bb.GetOrDefault(RtsDemoKeys.CurrentAction, "") is "Advance" or "Attack");

        Assert.True(retreated, "The damaged ship should first choose Retreat while the enemy is close.");
        Assert.True(recovered, "The damaged ship should eventually leave Retreat and rejoin combat after it reaches a safe distance.");
    }

    [Fact]
    public void DemoSimulation_ResetIsDeterministic()
    {
        var simulation = RtsDemoSimulation.Create();
        var initial = Snapshot(simulation);

        simulation.Step(0.5f);
        simulation.Reset();

        Assert.Equal(initial, Snapshot(simulation));
    }

    [Fact]
    public void DemoSimulation_NoLlmNetworkDependencies()
    {
        var sampleReferences = typeof(RtsDemoSimulation).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        Assert.DoesNotContain("Dominatus.Llm.OptFlow", sampleReferences);
        Assert.DoesNotContain("System.Net.Http", sampleReferences);
    }

    private static List<string> RunActions(RtsDemoSimulation simulation, ShipVisualState ship, float seconds, float dt)
    {
        var actions = new List<string>();
        var steps = (int)MathF.Ceiling(seconds / dt);
        for (var i = 0; i < steps; i++)
        {
            simulation.Step(dt);
            actions.Add(ship.Agent.Bb.GetOrDefault(RtsDemoKeys.CurrentAction, ""));
        }

        return actions;
    }

    private static bool RunUntil(RtsDemoSimulation simulation, float seconds, float dt, Func<bool> predicate)
    {
        var steps = (int)MathF.Ceiling(seconds / dt);
        for (var i = 0; i < steps; i++)
        {
            simulation.Step(dt);
            if (predicate())
                return true;
        }

        return false;
    }

    private static int CountSameFactionPairsWithin(RtsDemoSimulation simulation, float pixels)
    {
        var thresholdSquared = pixels * pixels;
        var pairs = 0;
        var alive = simulation.Ships.Where(s => s.Alive).ToArray();
        for (var i = 0; i < alive.Length; i++)
        {
            for (var j = i + 1; j < alive.Length; j++)
            {
                if (alive[i].Faction == alive[j].Faction && Vector2.DistanceSquared(alive[i].Position, alive[j].Position) <= thresholdSquared)
                    pairs++;
            }
        }

        return pairs;
    }

    private static string HomeSnapshot(RtsDemoSimulation simulation)
        => string.Join(";", simulation.Ships.Select(s => $"{s.AgentId.Value}:{s.Faction}:{s.Class}:{s.HomePosition.X:0.###},{s.HomePosition.Y:0.###}"));

    private static string Snapshot(RtsDemoSimulation simulation)
        => string.Join(";", simulation.Ships.Select(s => $"{s.AgentId.Value}:{s.Faction}:{s.Class}:{s.Position.X:0.###},{s.Position.Y:0.###}:{s.HomePosition.X:0.###},{s.HomePosition.Y:0.###}:{s.Hull:0.###}:{s.Alive}"));
}
