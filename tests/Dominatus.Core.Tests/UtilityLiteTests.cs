using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Runtime;
using Dominatus.Llm.OptFlow;
using Dominatus.OptFlow;
using Dominatus.UtilityLite;

namespace Dominatus.Core.Tests;

public class UtilityLiteTests
{
    static readonly BbKey<bool> Flag = new("Flag");
    static readonly BbKey<float> Threat = new("Threat");

    [Fact]
    public void Utility_When_Bb_And_Not_ComposeAsExpected()
    {
        var world = new AiWorld();
        var agent = new AiAgent(new HfsmInstance(new HfsmGraph { Root = "Root" }));

        agent.Bb.Set(Flag, true);
        agent.Bb.Set(Threat, 0.25f);

        var activeThreat = Utility.Bb(Threat);
        var isFlagged = Utility.Bb(Flag);
        var decision = Utility.All(isFlagged, Utility.Not(activeThreat));

        Assert.Equal(0.75f, decision.Eval(world, agent), 3);
    }

    [Fact]
    public void Utility_Remap_And_Pow_AdjustCurve()
    {
        var world = new AiWorld();
        var agent = new AiAgent(new HfsmInstance(new HfsmGraph { Root = "Root" }));

        agent.Bb.Set(Threat, 0.5f);
        var source = Utility.Bb(Threat);

        var remapped = Utility.Remap(source, 0.2f, 0.8f);
        var curved = Utility.Pow(remapped, 2f);

        Assert.Equal(0.5f, remapped.Eval(world, agent), 3);
        Assert.Equal(0.25f, curved.Eval(world, agent), 3);
    }

    [Fact]
    public void Utility_Option_And_Policy_SupportAiAndLlmDecide()
    {
        var options = new[]
        {
            Utility.Option("Patrol", Utility.Always, "Patrol"),
            Utility.Option("Idle", Utility.Never, "Idle")
        };

        var policy = Utility.Policy(hysteresis: 0.2f, minCommitSeconds: 1.5f, tieEpsilon: 0.001f);
        var aiStep = Ai.Decide(Utility.Slot("CombatLoop"), options,
            hysteresis: policy.Hysteresis,
            minCommitSeconds: policy.MinCommitSeconds,
            tieEpsilon: policy.TieEpsilon);
        var llmStep = llm.Decide(Utility.Slot("CombatLoop"), options,
            hysteresis: policy.Hysteresis,
            minCommitSeconds: policy.MinCommitSeconds,
            tieEpsilon: policy.TieEpsilon);

        Assert.Equal("CombatLoop", aiStep.Slot.Id);
        Assert.Equal("CombatLoop", llmStep.Slot.Id);
        Assert.Equal(aiStep.Policy, llmStep.Policy);
        Assert.Equal(2, llmStep.Options.Count);
    }
}
