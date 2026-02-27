using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.Llm.OptFlow;
using Dominatus.OptFlow;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed record DummyLlmOutput(string ToolName, string InputJson, float Score);

public static class ChatGPTInferiorTesting
{
    public static readonly BbKey<ActuationId> LastToolCallId = new("LastToolCallId");
    public static readonly BbKey<string> LastToolResult = new("LastToolResult");

    public static DummyLlmOutput SelectOutput(
        IReadOnlyList<DummyLlmOutput> options,
        DecisionPolicy policy,
        DummyLlmOutput? current,
        float elapsedSinceSwitchSeconds)
    {
        if (options.Count == 0)
            throw new ArgumentException("At least one output is required.", nameof(options));

        var best = options
            .OrderByDescending(x => x.Score)
            .First();

        if (current is null)
            return best;

        if (elapsedSinceSwitchSeconds < policy.MinCommitSeconds)
            return current;

        if (best.ToolName == current.ToolName)
            return best;

        if (best.Score <= current.Score + policy.Hysteresis)
            return current;

        if (MathF.Abs(best.Score - current.Score) <= policy.TieEpsilon)
            return current;

        return best;
    }

    public static IEnumerable<AiStep> EmitToolUsePlan(
        DummyLlmOutput selected,
        BbKey<ActuationId> idKey,
        BbKey<string> payloadKey)
    {
        yield return llm.Call(selected.ToolName, selected.InputJson, idKey);
        yield return llm.Await(idKey, payloadKey);
        yield return Ai.Succeed("tool completed");
    }
}

public sealed class ChatGPTInferiorTestingTests
{
    private sealed class RecordingActuator : IAiActuator
    {
        private long _nextId = 1;

        public List<IActuationCommand> Commands { get; } = [];

        public ActuationDispatchResult Dispatch(AiCtx ctx, IActuationCommand command)
        {
            Commands.Add(command);

            var id = new ActuationId(_nextId++);
            var payload = command is LlmToolCall tool
                ? $"executed:{tool.Name}"
                : "unknown";

            return new ActuationDispatchResult(id, Accepted: true, Completed: true, Ok: true, Error: null, Payload: payload);
        }
    }

    [Fact]
    public void SelectOutput_RespectsDecisionPolicyCommitWindowAndHysteresis()
    {
        var policy = new DecisionPolicy(Hysteresis: 0.15f, MinCommitSeconds: 1.00f, TieEpsilon: 0.001f);
        var current = new DummyLlmOutput("search", "{}", 0.70f);

        var options = new[]
        {
            new DummyLlmOutput("search", "{}", 0.70f),
            new DummyLlmOutput("write_file", "{\"path\":\"a.txt\"}", 0.80f)
        };

        var insideCommitWindow = ChatGPTInferiorTesting.SelectOutput(options, policy, current, elapsedSinceSwitchSeconds: 0.50f);
        Assert.Equal("search", insideCommitWindow.ToolName);

        var outsideCommitButWithinHysteresis = ChatGPTInferiorTesting.SelectOutput(options, policy, current, elapsedSinceSwitchSeconds: 1.10f);
        Assert.Equal("search", outsideCommitButWithinHysteresis.ToolName);

        var clearWinner = ChatGPTInferiorTesting.SelectOutput(
            [..options, new DummyLlmOutput("write_file", "{\"path\":\"b.txt\"}", 0.91f)],
            policy,
            current,
            elapsedSinceSwitchSeconds: 1.10f);

        Assert.Equal("write_file", clearWinner.ToolName);
    }

    [Fact]
    public void ToolPlan_DispatchesLlmToolCall_AndStoresTypedPayload()
    {
        var actuator = new RecordingActuator();
        var world = new AiWorld(actuator);

        var selected = new DummyLlmOutput("diag.ask", "{\"question\":\"status?\"}", 0.95f);

        static IEnumerator<AiStep> Root(AiCtx ctx)
        {
            yield return Ai.Push("Infer", "boot");
            while (true) yield return Ai.Wait(999f);
        }

        IEnumerator<AiStep> Infer(AiCtx ctx)
            => ChatGPTInferiorTesting.EmitToolUsePlan(selected, ChatGPTInferiorTesting.LastToolCallId, ChatGPTInferiorTesting.LastToolResult)
                .GetEnumerator();

        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = Root });
        graph.Add(new HfsmStateDef { Id = "Infer", Node = Infer });

        var brain = new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true });
        var agent = new AiAgent(brain);
        world.Add(agent);

        for (int i = 0; i < 8; i++)
            world.Tick(0.01f);

        Assert.Single(actuator.Commands);
        Assert.IsType<LlmToolCall>(actuator.Commands[0]);

        var result = agent.Bb.GetOrDefault(ChatGPTInferiorTesting.LastToolResult, string.Empty);
        Assert.Equal("executed:diag.ask", result);
    }
}
