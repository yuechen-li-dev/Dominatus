using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Xunit;

namespace Dominatus.UtilityLite.Tests;

public sealed class WhenFacadeTests
{
    private static readonly BbKey<bool> BoolKey = new("When.Bool");
    private static readonly BbKey<float> FloatKey = new("When.Float");
    private static readonly BbKey<string> StringKey = new("When.String");

    [Fact]
    public void Always_MatchesUtilityAlways()
    {
        var (_, agent) = CreateWorldAndAgent();

        var expected = Utility.Always.Eval(default!, agent);
        var actual = When.Always.Eval(default!, agent);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Never_MatchesUtilityNever()
    {
        var (_, agent) = CreateWorldAndAgent();

        var expected = Utility.Never.Eval(default!, agent);
        var actual = When.Never.Eval(default!, agent);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BbBool_MatchesUtilityBbBool()
    {
        var (_, agent) = CreateWorldAndAgent();
        agent.Bb.Set(BoolKey, true);

        var expected = Utility.Bb(BoolKey).Eval(default!, agent);
        var actual = When.Bb(BoolKey).Eval(default!, agent);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BbFloat_MatchesUtilityBbFloat()
    {
        var (_, agent) = CreateWorldAndAgent();
        agent.Bb.Set(FloatKey, 0.6f);

        var expected = Utility.Bb(FloatKey).Eval(default!, agent);
        var actual = When.Bb(FloatKey).Eval(default!, agent);

        Assert.Equal(expected, actual, 3);
    }

    [Fact]
    public void BbEq_MatchesUtilityBbEq()
    {
        var (_, agent) = CreateWorldAndAgent();
        agent.Bb.Set(StringKey, "combat");

        var expected = Utility.BbEq(StringKey, "combat").Eval(default!, agent);
        var actual = When.BbEq(StringKey, "combat").Eval(default!, agent);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Not_MatchesUtilityNot()
    {
        var (_, agent) = CreateWorldAndAgent();

        var source = When.Score((_, _) => 0.25f);

        var expected = Utility.Not(source).Eval(default!, agent);
        var actual = When.Not(source).Eval(default!, agent);

        Assert.Equal(expected, actual, 3);
    }

    [Fact]
    public void All_MatchesUtilityAll()
    {
        var (_, agent) = CreateWorldAndAgent();

        var a = When.Score((_, _) => 0.5f);
        var b = When.Score((_, _) => 0.8f);

        var expected = Utility.All(a, b).Eval(default!, agent);
        var actual = When.All(a, b).Eval(default!, agent);

        Assert.Equal(expected, actual, 3);
    }

    [Fact]
    public void Any_MatchesUtilityAny()
    {
        var (_, agent) = CreateWorldAndAgent();

        var a = When.Score((_, _) => 0.2f);
        var b = When.Score((_, _) => 0.7f);

        var expected = Utility.Any(a, b).Eval(default!, agent);
        var actual = When.Any(a, b).Eval(default!, agent);

        Assert.Equal(expected, actual, 3);
    }

    [Fact]
    public void Threshold_MatchesUtilityThreshold()
    {
        var (_, agent) = CreateWorldAndAgent();

        var source = When.Score((_, _) => 0.8f);

        var expected = Utility.Threshold(source, 0.5f).Eval(default!, agent);
        var actual = When.Threshold(source, 0.5f).Eval(default!, agent);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WhenSurface_WorksInSimpleDecisionAuthoring()
    {
        var world = new AiWorld();
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });
        graph.Add(new HfsmStateDef { Id = "Patrol", Node = PatrolNode });
        graph.Add(new HfsmStateDef { Id = "Combat", Node = CombatNode });

        var brain = new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true });
        var agent = new AiAgent(brain);
        world.Add(agent);

        for (int i = 0; i < 10; i++)
            world.Tick(0.1f);

        Assert.Equal("Patrol", brain.GetActivePath()[^1].Value);

        agent.Bb.Set(BoolKey, true);

        for (int i = 0; i < 10; i++)
            world.Tick(0.1f);

        Assert.Equal("Combat", brain.GetActivePath()[^1].Value);
    }

    private static (AiWorld world, AiAgent agent) CreateWorldAndAgent()
    {
        var world = new AiWorld();
        var graph = new HfsmGraph { Root = "Idle" };
        graph.Add(new HfsmStateDef { Id = "Idle", Node = IdleNode });

        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);

        return (world, agent);
    }

    private static IEnumerator<AiStep> IdleNode(AiCtx _)
    {
        while (true)
            yield return Ai.Wait(999f);
    }

    private static IEnumerator<AiStep> RootNode(AiCtx _)
    {
        while (true)
        {
            yield return Ai.Decide(
            [
                Ai.Option("Combat", When.Bb(BoolKey), "Combat"),
                Ai.Option("Patrol", When.Score((_, _) => 0.4f), "Patrol"),
            ],
            hysteresis: 0f,
            minCommitSeconds: 0f,
            tieEpsilon: 0f);
        }
    }

    private static IEnumerator<AiStep> PatrolNode(AiCtx _)
    {
        while (true)
            yield return Ai.Wait(0.01f);
    }

    private static IEnumerator<AiStep> CombatNode(AiCtx _)
    {
        while (true)
            yield return Ai.Wait(0.01f);
    }
}