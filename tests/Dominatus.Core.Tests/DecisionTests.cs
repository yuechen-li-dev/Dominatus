using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Xunit;

namespace Dominatus.Core.Tests;

public class DecisionTests
{
    static readonly BbKey<float> AScore = new("AScore");
    static readonly BbKey<float> BScore = new("BScore");

    static Consideration FromKey(BbKey<float> key)
        => new((w, a) => a.Bb.GetOrDefault(key, 0f));

    static IEnumerator<AiStep> Root(AiCtx ctx)
    {
        while (true)
        {
            yield return Ai.Wait(0.10f); // 10Hz cadence
            yield return Ai.Decide([
                Ai.Option("A", FromKey(AScore), "A"),
                Ai.Option("B", FromKey(BScore), "B"),
            ], hysteresis: 0.10f, minCommitSeconds: 0.75f);
        }
    }

    static IEnumerator<AiStep> Loop(AiCtx ctx)
    {
        while (true) yield return new WaitSeconds(999f);
    }

    static HfsmInstance MakeBrain()
    {
        var g = new HfsmGraph { Root = "Root" };
        g.Add(new HfsmStateDef { Id = "Root", Node = Root });
        g.Add(new HfsmStateDef { Id = "A", Node = Loop });
        g.Add(new HfsmStateDef { Id = "B", Node = Loop });

        return new HfsmInstance(g, new HfsmOptions { KeepRootFrame = true });
    }

    [Fact]
    public void Decide_RespectsMinCommit()
    {
        var world = new AiWorld();
        var brain = MakeBrain();
        var agent = new AiAgent(brain);
        world.Add(agent);

        agent.Bb.Set(AScore, 1.0f);
        agent.Bb.Set(BScore, 0.0f);

        // Tick enough to decide and push A
        for (int i = 0; i < 3; i++) world.Tick(0.10f);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"A" }, brain.GetActivePath());

        // Now make B much better, but within min-commit window
        agent.Bb.Set(BScore, 1.0f);
        agent.Bb.Set(AScore, 0.0f);

        // Advance less than 0.75s total since last switch; should NOT switch
        for (int i = 0; i < 6; i++) world.Tick(0.10f); // 0.6s
        Assert.Equal(new[] { (StateId)"Root", (StateId)"A" }, brain.GetActivePath());

        // Advance beyond min-commit; should switch to B
        for (int i = 0; i < 3; i++) world.Tick(0.10f); // +0.3s => 0.9s
        Assert.Equal(new[] { (StateId)"Root", (StateId)"B" }, brain.GetActivePath());
    }

    [Fact]
    public void Decide_RespectsHysteresis()
    {
        var world = new AiWorld();
        var brain = MakeBrain();
        var agent = new AiAgent(brain);
        world.Add(agent);

        // Start with A strong, pick A
        agent.Bb.Set(AScore, 0.60f);
        agent.Bb.Set(BScore, 0.00f);

        for (int i = 0; i < 3; i++) world.Tick(0.10f);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"A" }, brain.GetActivePath());

        // Wait past commit window so only hysteresis matters
        for (int i = 0; i < 10; i++) world.Tick(0.10f); // 1.0s

        // B becomes slightly better but not by hysteresis margin (0.10)
        agent.Bb.Set(AScore, 0.60f);
        agent.Bb.Set(BScore, 0.65f); // +0.05 only

        for (int i = 0; i < 3; i++) world.Tick(0.10f);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"A" }, brain.GetActivePath());

        // Now B beats A by >= 0.10, should switch
        agent.Bb.Set(BScore, 0.71f); // +0.11
        for (int i = 0; i < 3; i++) world.Tick(0.10f);
        Assert.Equal(new[] { (StateId)"Root", (StateId)"B" }, brain.GetActivePath());
    }
}