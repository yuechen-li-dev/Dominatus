using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;

namespace Dominatus.MonoGameConn.Tests;

internal static class TestAgentFactory
{
    public static AiAgent CreateNoopAgent() => new(BuildNoopBrain());

    public static AiAgent CreateCountingAgent(BbKey<int> tickCountKey) => new(BuildCountingBrain(tickCountKey));

    private static HfsmInstance BuildNoopBrain()
    {
        static IEnumerator<AiStep> Loop(AiCtx _)
        {
            while (true)
                yield return null!;
        }

        var graph = new HfsmGraph { Root = "idle" };
        graph.Add(new HfsmStateDef { Id = "idle", Node = Loop });
        return new HfsmInstance(graph);
    }

    private static HfsmInstance BuildCountingBrain(BbKey<int> tickCountKey)
    {
        IEnumerator<AiStep> Loop(AiCtx ctx)
        {
            while (true)
            {
                ctx.Bb.Set(tickCountKey, ctx.Bb.GetOrDefault(tickCountKey, 0) + 1);
                yield return null!;
            }
        }

        var graph = new HfsmGraph { Root = "count" };
        graph.Add(new HfsmStateDef { Id = "count", Node = Loop });
        return new HfsmInstance(graph);
    }
}
