using System.Text.Json;
using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmMagiRefusalRuntimeTests
{
    private static readonly BbKey<string> ChosenKey = new("magi.refusal.chosen");
    private static readonly BbKey<string> RationaleKey = new("magi.refusal.rationale");
    private static readonly BbKey<string> ResultJsonKey = new("magi.refusal.result");
    private static readonly BbKey<string> RefusalKey = new("magi.refusal.reason");
    private static readonly BbKey<string> ProposalKey = new("magi.refusal.proposal");

    [Fact]
    public void MagiDecide_Refused_StoresRefusalReason_WhenConfigured()
    {
        var (_, _, _, ctx) = Setup(refusal: new("unsafe objective"), policy: new LlmMagiRefusalPolicy(StoreRefusalReasonAs: RefusalKey));
        Execute(ctx, Step(refusal: new LlmMagiRefusalPolicy(StoreRefusalReasonAs: RefusalKey)));
        Assert.Equal("unsafe objective", ctx.Bb.GetOrDefault(RefusalKey, ""));
        Assert.False(ctx.Bb.TryGet(ChosenKey, out _));
    }

    [Fact]
    public void MagiDecide_Refused_StoresProposedAlternative_WhenAllowedAndConfigured()
    {
        var policy = new LlmMagiRefusalPolicy(AllowProposedAlternative: true, StoreRefusalReasonAs: RefusalKey, StoreProposedAlternativeAs: ProposalKey);
        var (_, _, _, ctx) = Setup(refusal: new("unsafe objective", "request human arbitration"), policy: policy);
        Execute(ctx, Step(refusal: policy));
        Assert.Equal("request human arbitration", ctx.Bb.GetOrDefault(ProposalKey, ""));
        Assert.False(ctx.Bb.TryGet(ChosenKey, out _));
    }

    [Fact]
    public void MagiDecide_Refused_FailsWhenUnobservable()
    {
        var (_, _, _, ctx) = Setup(refusal: new("unsafe objective"), policy: new LlmMagiRefusalPolicy());
        var ex = Assert.Throws<InvalidOperationException>(() => Execute(ctx, Step(storeResult: false, refusal: new LlmMagiRefusalPolicy())));
        Assert.Contains("unobservable", ex.Message, StringComparison.OrdinalIgnoreCase);
        AssertNoOutputs(ctx);
    }

    [Fact]
    public void MagiDecide_Refused_WithProposalRejected_WhenProposalNotAllowed()
    {
        var policy = new LlmMagiRefusalPolicy(StoreRefusalReasonAs: RefusalKey, StoreProposedAlternativeAs: ProposalKey);
        var (_, _, _, ctx) = Setup(refusal: new("unsafe objective", "alternative"), policy: policy);
        Assert.Throws<InvalidOperationException>(() => Execute(ctx, Step(refusal: policy)));
        AssertNoOutputs(ctx);
    }

    [Fact]
    public void MagiDecide_Refused_WithProposalAccepted_WhenProposalAllowed()
    {
        var policy = new LlmMagiRefusalPolicy(AllowProposedAlternative: true, StoreRefusalReasonAs: RefusalKey, StoreProposedAlternativeAs: ProposalKey);
        var (_, _, _, ctx) = Setup(refusal: new("unsafe objective", "alternative"), policy: policy);
        Execute(ctx, Step(refusal: policy));
        Assert.Equal("alternative", ctx.Bb.GetOrDefault(ProposalKey, ""));
    }

    [Fact]
    public void MagiDecide_Refused_ReentryDoesNotRedispatchMagi()
    {
        var (a, b, j, ctx) = Setup(refusal: new("unsafe objective"), policy: new LlmMagiRefusalPolicy(StoreRefusalReasonAs: RefusalKey));
        Execute(ctx, Step(refusal: new LlmMagiRefusalPolicy(StoreRefusalReasonAs: RefusalKey)));
        ctx.Bb.Set(RefusalKey, string.Empty);
        Execute(ctx, Step(refusal: new LlmMagiRefusalPolicy(StoreRefusalReasonAs: RefusalKey)));
        Assert.Equal(0, a.CallCount);
        Assert.Equal(0, b.CallCount);
        Assert.Equal(0, j.CallCount);
        Assert.Equal("unsafe objective", ctx.Bb.GetOrDefault(RefusalKey, ""));
    }


    [Fact]
    public void MagiDecide_RefusedReplay_SuppressesMagiProvider()
    {
        var (a, b, j, ctx) = Setup(refusal: new("unsafe objective"), policy: new LlmMagiRefusalPolicy(StoreRefusalReasonAs: RefusalKey));
        Execute(ctx, Step(refusal: new LlmMagiRefusalPolicy(StoreRefusalReasonAs: RefusalKey)));

        Assert.Equal(0, a.CallCount);
        Assert.Equal(0, b.CallCount);
        Assert.Equal(0, j.CallCount);
        Assert.False(ctx.Bb.TryGet(ChosenKey, out _));
        Assert.Equal("unsafe objective", ctx.Bb.GetOrDefault(RefusalKey, ""));
        Assert.True(ctx.Bb.TryGet(ResultJsonKey, out _));
    }

    [Fact]
    public void MagiDecide_Refused_StoresResultJsonWithRefusal()
    {
        var policy = new LlmMagiRefusalPolicy(AllowProposedAlternative: true, StoreRefusalReasonAs: RefusalKey, StoreProposedAlternativeAs: ProposalKey);
        var (_, _, _, ctx) = Setup(refusal: new("unsafe objective", "alternative"), policy: policy);
        Execute(ctx, Step(refusal: policy));
        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        var root = doc.RootElement;
        Assert.Equal("refused", root.GetProperty("outcome").GetString());
        Assert.Equal("unsafe objective", root.GetProperty("refusal").GetProperty("reason").GetString());
        Assert.Equal("alternative", root.GetProperty("refusal").GetProperty("proposedAlternative").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("chosenOptionId").ValueKind);
        Assert.True(root.TryGetProperty("requestHash", out _));
    }

    private static AiStep Step(bool storeResult = true, LlmMagiRefusalPolicy? refusal = null)
    {
        var p = Participants();
        return Llm.MagiDecide("magi.refusal", "intent", "persona", b => b.Add("k", "v"), Options(), p.A, p.B, p.J, ChosenKey, RationaleKey, storeResult ? ResultJsonKey : null, refusal: refusal);
    }

    private static (FakeLlmDecisionClient A, FakeLlmDecisionClient B, FakeLlmMagiJudgeClient J, AiCtx Ctx) Setup(LlmDecisionRefusal refusal, LlmMagiRefusalPolicy policy)
    {
        var p = Participants();
        var request = new LlmMagiRequest("magi.refusal", "intent", "persona", "{\"k\":\"v\"}", Options(), p.A, p.B, p.J, policy.AllowProposedAlternative, policy.MaxReasonChars, policy.MaxProposedAlternativeChars, LlmMagiRequest.DefaultPromptTemplateVersion, LlmMagiRequest.DefaultOutputContractVersion);
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);
        var a = new FakeLlmDecisionClient(new LlmDecisionResult(LlmDecisionRequestHasher.ComputeHash(aReq), [new("join",0.9,1,"r"),new("mediate",0.5,2,"r"),new("refuse",0.2,3,"r")], "a"));
        var b = new FakeLlmDecisionClient(new LlmDecisionResult(LlmDecisionRequestHasher.ComputeHash(bReq), [new("mediate",0.9,1,"r"),new("join",0.5,2,"r"),new("refuse",0.2,3,"r")], "b"));
        var j = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", p.A.Id, "judge"));
        var cassette = new InMemoryLlmMagiCassette();
        var aRes = new LlmDecisionResult(LlmDecisionRequestHasher.ComputeHash(aReq), [new("join",0.9,1,"r"),new("mediate",0.5,2,"r"),new("refuse",0.2,3,"r")], "a");
        var bRes = new LlmDecisionResult(LlmDecisionRequestHasher.ComputeHash(bReq), [new("mediate",0.9,1,"r"),new("join",0.5,2,"r"),new("refuse",0.2,3,"r")], "b");
        cassette.Put(LlmMagiRequestHasher.ComputeHash(request), request, new LlmMagiDecisionResult(LlmMagiRequestHasher.ComputeHash(request), request.AdvocateA, request.AdvocateB, request.Judge, aRes, bRes, new LlmMagiJudgment(null, p.A.Id, "judge", LlmDecisionOutcome.Refused, refusal), LlmDecisionOutcome.Refused, refusal));
        var host = new ActuatorHost();
        host.Register(new LlmMagiDecisionHandler(a, b, j, cassette, LlmCassetteMode.Replay));
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = _ => Empty() });
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);
        return (a, b, j, new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator, new LiveWorldBb(world.Bb)));
    }

    private static void AssertNoOutputs(AiCtx ctx)
    {
        Assert.False(ctx.Bb.TryGet(ChosenKey, out _));
        Assert.False(ctx.Bb.TryGet(RationaleKey, out _));
        Assert.False(ctx.Bb.TryGet(ResultJsonKey, out _));
        Assert.False(ctx.Bb.TryGet(RefusalKey, out _));
        Assert.False(ctx.Bb.TryGet(ProposalKey, out _));
    }

    private static void Execute(AiCtx ctx, AiStep step) { var cursor = default(EventCursor); for (var i=0;i<8;i++) if (((IWaitEvent)step).TryConsume(ctx, ref cursor)) return; throw new TimeoutException(); }
    private static (LlmMagiParticipant A, LlmMagiParticipant B, LlmMagiParticipant J) Participants() => (Llm.MagiParticipant("advA","openai","gpt-5","a"), Llm.MagiParticipant("advB","anthropic","claude","b"), Llm.MagiParticipant("judge","gemini","g3","j"));
    private static IReadOnlyList<LlmDecisionOption> Options() => [Llm.Option("join","Join"),Llm.Option("mediate","Mediate"),Llm.Option("refuse","Refuse")];
    private static IEnumerator<AiStep> Empty() { yield break; }
}
