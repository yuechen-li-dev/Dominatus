using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Dominatus.UtilityLite;
using Xunit;

namespace Dominatus.UtilityLite.Tests;

public sealed class UtilityLiteTests
{
    private static readonly BbKey<bool> BoolKey = new("Test.Bool");
    private static readonly BbKey<float> FloatKey = new("Test.Float");
    private static readonly BbKey<int> IntKey = new("Test.Int");
    private static readonly BbKey<string> StringKey = new("Test.String");

    [Fact]
    public void Always_EvaluatesToOne()
    {
        var (_, agent) = CreateWorldAndAgent();
        Assert.Equal(1f, Utility.Always.Eval(default!, agent));
    }

    [Fact]
    public void Never_EvaluatesToZero()
    {
        var (_, agent) = CreateWorldAndAgent();
        Assert.Equal(0f, Utility.Never.Eval(default!, agent));
    }

    [Fact]
    public void Not_InvertsScore()
    {
        var (_, agent) = CreateWorldAndAgent();

        var source = Utility.Score((_, _) => 0.25f);
        var result = Utility.Not(source).Eval(default!, agent);

        Assert.Equal(0.75f, result, 3);
    }

    [Fact]
    public void All_MultipliesScores()
    {
        var (_, agent) = CreateWorldAndAgent();

        var result = Utility.All(
            Utility.Score((_, _) => 0.5f),
            Utility.Score((_, _) => 0.8f),
            Utility.Score((_, _) => 0.25f))
            .Eval(default!, agent);

        Assert.Equal(0.1f, result, 3);
    }

    [Fact]
    public void Any_TakesMaximumScore()
    {
        var (_, agent) = CreateWorldAndAgent();

        var result = Utility.Any(
            Utility.Score((_, _) => 0.2f),
            Utility.Score((_, _) => 0.7f),
            Utility.Score((_, _) => 0.4f))
            .Eval(default!, agent);

        Assert.Equal(0.7f, result, 3);
    }

    [Fact]
    public void Threshold_ReturnsOne_WhenSourceMeetsThreshold()
    {
        var (_, agent) = CreateWorldAndAgent();

        var result = Utility.Threshold(
            Utility.Score((_, _) => 0.8f),
            0.5f)
            .Eval(default!, agent);

        Assert.Equal(1f, result);
    }

    [Fact]
    public void Threshold_ReturnsZero_WhenSourceBelowThreshold()
    {
        var (_, agent) = CreateWorldAndAgent();

        var result = Utility.Threshold(
            Utility.Score((_, _) => 0.3f),
            0.5f)
            .Eval(default!, agent);

        Assert.Equal(0f, result);
    }

    [Fact]
    public void Remap_MapsNormalizedSourceRangeIntoZeroToOne()
    {
        var (_, agent) = CreateWorldAndAgent();

        // Remap operates on Consideration outputs, which are already clamped to 0..1.
        // So test it as a shaping helper over normalized input, not as a raw 10..20 mapper.
        var result = Utility.Remap(
            Utility.Score((_, _) => 0.5f),
            0.25f,
            0.75f)
            .Eval(default!, agent);

        Assert.Equal(0.5f, result, 3);
    }

    [Fact]
    public void Pow_ShapesScore()
    {
        var (_, agent) = CreateWorldAndAgent();

        var result = Utility.Pow(
            Utility.Score((_, _) => 0.5f),
            2f)
            .Eval(default!, agent);

        Assert.Equal(0.25f, result, 3);
    }

    [Fact]
    public void BbBool_ReadsFalseWhenUnset()
    {
        var (_, agent) = CreateWorldAndAgent();

        var result = Utility.Bb(BoolKey).Eval(default!, agent);

        Assert.Equal(0f, result);
    }

    [Fact]
    public void BbBool_ReadsTrueAsOne()
    {
        var (_, agent) = CreateWorldAndAgent();
        agent.Bb.Set(BoolKey, true);

        var result = Utility.Bb(BoolKey).Eval(default!, agent);

        Assert.Equal(1f, result);
    }

    [Fact]
    public void BbFloat_ReadsRawFloatScore()
    {
        var (_, agent) = CreateWorldAndAgent();
        agent.Bb.Set(FloatKey, 0.65f);

        var result = Utility.Bb(FloatKey).Eval(default!, agent);

        Assert.Equal(0.65f, result, 3);
    }

    [Fact]
    public void BbIntRange_RemapsIntoZeroToOne()
    {
        var (_, agent) = CreateWorldAndAgent();
        agent.Bb.Set(IntKey, 5);

        var result = Utility.Bb(IntKey, 0, 10).Eval(default!, agent);

        Assert.Equal(0.5f, result, 3);
    }

    [Fact]
    public void BbAtLeast_ReturnsOne_WhenThresholdMet()
    {
        var (_, agent) = CreateWorldAndAgent();
        agent.Bb.Set(FloatKey, 0.8f);

        var result = Utility.BbAtLeast(FloatKey, 0.5f).Eval(default!, agent);

        Assert.Equal(1f, result);
    }

    [Fact]
    public void BbAtLeast_ReturnsZero_WhenThresholdNotMet()
    {
        var (_, agent) = CreateWorldAndAgent();
        agent.Bb.Set(FloatKey, 0.2f);

        var result = Utility.BbAtLeast(FloatKey, 0.5f).Eval(default!, agent);

        Assert.Equal(0f, result);
    }

    [Fact]
    public void BbAtMost_ReturnsOne_WhenThresholdMet()
    {
        var (_, agent) = CreateWorldAndAgent();
        agent.Bb.Set(FloatKey, 0.2f);

        var result = Utility.BbAtMost(FloatKey, 0.5f).Eval(default!, agent);

        Assert.Equal(1f, result);
    }

    [Fact]
    public void BbAtMost_ReturnsZero_WhenThresholdNotMet()
    {
        var (_, agent) = CreateWorldAndAgent();
        agent.Bb.Set(FloatKey, 0.8f);

        var result = Utility.BbAtMost(FloatKey, 0.5f).Eval(default!, agent);

        Assert.Equal(0f, result);
    }

    [Fact]
    public void BbEq_ReturnsTrue_WhenPresentValueMatches()
    {
        var (_, agent) = CreateWorldAndAgent();
        agent.Bb.Set(StringKey, "combat");

        var result = Utility.BbEq(StringKey, "combat").Eval(default!, agent);

        Assert.Equal(1f, result);
    }

    [Fact]
    public void BbEq_ReturnsFalse_WhenPresentValueDiffers()
    {
        var (_, agent) = CreateWorldAndAgent();
        agent.Bb.Set(StringKey, "patrol");

        var result = Utility.BbEq(StringKey, "combat").Eval(default!, agent);

        Assert.Equal(0f, result);
    }

    [Fact]
    public void BbEq_UnsetValue_ShouldNotSilentlyMatchExpected()
    {
        var (_, agent) = CreateWorldAndAgent();

        var result = Utility.BbEq(StringKey, "combat").Eval(default!, agent);

        Assert.Equal(0f, result);
    }

    [Fact]
    public void Decide_UsesUtilitySurface_ForSimplePatrolVsCombat()
    {
        var world = new AiWorld();
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });
        graph.Add(new HfsmStateDef { Id = "Patrol", Node = PatrolNode });
        graph.Add(new HfsmStateDef { Id = "Combat", Node = CombatNode });

        var brain = new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true });
        var agent = new AiAgent(brain);
        world.Add(agent);

        // Initial: not alerted -> Patrol
        for (int i = 0; i < 10; i++)
            world.Tick(0.1f);

        Assert.Equal("Patrol", brain.GetActivePath()[^1].Value);

        // Flip alert -> Combat should now cleanly beat Patrol's fallback score.
        agent.Bb.Set(BoolKey, true);

        for (int i = 0; i < 10; i++)
            world.Tick(0.1f);

        Assert.Equal("Combat", brain.GetActivePath()[^1].Value);

        static IEnumerator<AiStep> RootNode(AiCtx _)
        {
            while (true)
            {
                yield return Ai.Decide(
                [
                    Utility.Option("Combat", Utility.Bb(BoolKey), "Combat"),
                Utility.Option("Patrol", Utility.Score((_, _) => 0.4f), "Patrol"),
            ],
                hysteresis: 0f,
                minCommitSeconds: 0f,
                tieEpsilon: 0f);
            }
        }
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
                Utility.Option("Combat", Utility.Bb(BoolKey), "Combat"),
                Utility.Option("Patrol", Utility.Always, "Patrol"),
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