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
