using System.Text.Json;
using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmPromptCallTests
{
    private static readonly BbKey<string> TextKey = new("prompt.text");
    private static readonly BbKey<string> ResultJsonKey = new("prompt.resultJson");

    [Fact] public void LlmCall_RejectsMissingStableId() => Assert.Throws<ArgumentException>(() => Step(stableId: " "));
    [Fact] public void LlmCall_RejectsMissingIntent() => Assert.Throws<ArgumentException>(() => Step(intent: " "));
    [Fact] public void LlmCall_RejectsMissingPersona() => Assert.Throws<ArgumentException>(() => Step(persona: " "));
    [Fact] public void LlmCall_RejectsMissingStoreTextKey() => Assert.Throws<ArgumentException>(() => Llm.Call("id","intent","persona",b=>b.Add("x","y"),new BbKey<string>(" ")));

    [Fact]
    public void LlmCall_BuildsPromptRequestWithStableHash()
    {
        var req1 = BuildCommand().ToTextRequest();
        var req2 = BuildCommand().ToTextRequest();
        Assert.Equal(LlmRequestHasher.ComputeHash(req1), LlmRequestHasher.ComputeHash(req2));
    }

    [Fact]
    public void LlmCall_StoresTextResult()
    {
        var client = new FakeLlmClient("summary text");
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        ExecuteStep(Step(storeResultJson: false), ctx);
        Assert.Equal("summary text", ctx.Bb.GetOrDefault(TextKey, ""));
    }

    [Fact]
    public void LlmCall_StoresResultJson_WhenConfigured()
    {
        var client = new FakeLlmClient("summary text");
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        ExecuteStep(Step(storeResultJson: true), ctx);
        Assert.True(ctx.Bb.TryGet(ResultJsonKey, out var json));
        Assert.Contains("\"stableId\":\"summarize-ledger\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void LlmCall_ResultJsonIncludesRequestHashStableIdAndText()
    {
        var client = new FakeLlmClient("summary text");
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        ExecuteStep(Step(storeResultJson: true), ctx);
        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("requestHash", out _));
        Assert.Equal("summarize-ledger", root.GetProperty("stableId").GetString());
        Assert.Equal("summary text", root.GetProperty("text").GetString());
    }

    [Fact]
    public void LlmCall_ReentryAfterCompletedCall_DoesNotRedispatchProvider()
    {
        var client = new FakeLlmClient("first");
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        ExecuteStep(Step(storeResultJson: true), ctx);
        ctx.Bb.Set(TextKey, string.Empty);
        ExecuteStep(Step(storeResultJson: true), ctx);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public void LlmCall_ReentryAfterCompletedCall_RestoresOutputs()
    {
        var client = new FakeLlmClient("first");
        var (_, ctx) = CreateWorldAndCtx(client, LlmCassetteMode.Live);
        ExecuteStep(Step(storeResultJson: true), ctx);
        var originalJson = ctx.Bb.GetOrDefault(ResultJsonKey, "");
        ctx.Bb.Set(TextKey, string.Empty);
        ctx.Bb.Set(ResultJsonKey, string.Empty);
        ExecuteStep(Step(storeResultJson: true), ctx);
        Assert.Equal("first", ctx.Bb.GetOrDefault(TextKey, ""));
        Assert.Equal(originalJson, ctx.Bb.GetOrDefault(ResultJsonKey, ""));
    }

    private static AiStep Step(string stableId = "summarize-ledger", string intent = "summarize", string persona = "concise", bool storeResultJson = true)
        => Llm.Call(stableId, intent, persona, b => b.Add("ledger", "entry"), TextKey, storeResultJson ? ResultJsonKey : null);

    private static LlmPromptCommand BuildCommand() => new("summarize-ledger", "summarize", "concise", "{\"ledger\":\"entry\"}", Llm.DefaultSampling, LlmPromptCommand.DefaultPromptTemplateVersion, LlmPromptCommand.DefaultOutputContractVersion);

    private static (AiWorld World, AiCtx Ctx) CreateWorldAndCtx(FakeLlmClient client, LlmCassetteMode mode, ILlmCassette? cassette = null)
    {
        var host = new ActuatorHost();
        host.Register(new LlmTextActuationHandler(client, cassette ?? new InMemoryLlmCassette(), mode));
        var world = new AiWorld(host);

        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);

        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);
        return (world, ctx);
    }

    private static void ExecuteStep(AiStep step, AiCtx ctx)
    {
        var wait = Assert.IsAssignableFrom<IWaitEvent>(step);
        var cursor = default(EventCursor);
        if (!wait.TryConsume(ctx, ref cursor))
        {
            wait.TryConsume(ctx, ref cursor);
        }
    }

    private static IEnumerator<AiStep> RootNode(AiCtx _)
    {
        yield break;
    }
}
