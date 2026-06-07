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
        Assert.Equal(25, simulation.DominionAlive);
        Assert.Equal(25, simulation.CollectiveAlive);
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

    private static string Snapshot(RtsDemoSimulation simulation)
        => string.Join(";", simulation.Ships.Select(s => $"{s.AgentId.Value}:{s.Faction}:{s.Position.X:0.###},{s.Position.Y:0.###}:{s.Hull:0.###}:{s.Alive}"));
}
