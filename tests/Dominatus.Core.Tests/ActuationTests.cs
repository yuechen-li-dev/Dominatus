using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Xunit;

namespace Dominatus.Core.Tests;

public sealed class ActuationTests
{
    private sealed record LogCommand(string Message) : IActuationCommand;

    private sealed class TestActuator : IAiActuator
    {
        private long _next = 1;

        public ActuationDispatchResult Dispatch(AiCtx ctx, IActuationCommand command)
        {
            var id = new ActuationId(_next++);

            // Immediately complete successfully
            return new ActuationDispatchResult(
                Id: id,
                Accepted: true,
                Completed: true,
                Ok: true,
                Payload: command);
        }
    }

    private static readonly BbKey<ActuationId> LastActId = new("LastActId");

    private static IEnumerator<AiStep> Root(AiCtx ctx)
    {
        yield return Ai.Push("Do", "boot");
        while (true) yield return Ai.Wait(999f);
    }

    private static IEnumerator<AiStep> Do(AiCtx ctx)
    {
        yield return Ai.Act(new LogCommand("hello"), storeIdAs: LastActId);
        yield return Ai.Await(LastActId);
        yield return Ai.Goto("Done", "act completed");
    }

    private static IEnumerator<AiStep> Done(AiCtx ctx)
    {
        while (true) yield return Ai.Wait(999f);
    }

    static void TickUntil(AiWorld world, Func<bool> cond, int maxTicks = 20, float dt = 0.01f)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            if (cond()) return;
            world.Tick(dt);
        }
        Assert.Fail("Condition not reached in time.");
    }

    [Fact]
    public void Act_StoresId_AndAwaitReceivesCompletionEvent()
    {
        // Use AiWorld but swap its actuator after construction (simple test hook).
        // If your AiWorld.Actuator is readonly, make AiWorld accept an actuator in constructor.
        var world = new AiWorld(new TestActuator());

        var g = new HfsmGraph { Root = "Root" };
        g.Add(new HfsmStateDef { Id = "Root", Node = Root });
        g.Add(new HfsmStateDef { Id = "Do", Node = Do });
        g.Add(new HfsmStateDef { Id = "Done", Node = Done });

        var brain = new HfsmInstance(g, new HfsmOptions { KeepRootFrame = true });
        var agent = new AiAgent(brain);
        world.Add(agent);

        // Tick a few times to progress Do -> Done
        TickUntil(world, () =>
        {
            var path = brain.GetActivePath();
            return path.Count == 2 && path[1].Equals((StateId)"Done");
        });

        Assert.Equal([(StateId)"Root", (StateId)"Done"], brain.GetActivePath());

        var id = agent.Bb.GetOrDefault(LastActId, default);
        Assert.NotEqual(default, id);
    }
}