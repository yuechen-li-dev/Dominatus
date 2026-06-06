using Dominatus.MonoGameConn;
using Dominatus.MonoGameRtsDemo;

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

    private static string Snapshot(RtsDemoSimulation simulation)
        => string.Join(";", simulation.Ships.Select(s => $"{s.AgentId.Value}:{s.Faction}:{s.Position.X:0.###},{s.Position.Y:0.###}:{s.Hull:0.###}:{s.Alive}"));
}
