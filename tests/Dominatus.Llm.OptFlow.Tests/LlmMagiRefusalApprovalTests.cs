using System.Text.Json;
using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmMagiRefusalApprovalTests
{
    private static readonly BbKey<string> ChosenKey = new("magi.refusal.approval.chosen");
    private static readonly BbKey<string> RationaleKey = new("magi.refusal.approval.rationale");
    private static readonly BbKey<string> ResultJsonKey = new("magi.refusal.approval.result");
    private static readonly BbKey<string> RefusalKey = new("magi.refusal.approval.reason");
    private static readonly BbKey<string> ProposalKey = new("magi.refusal.approval.proposal");

    [Fact]
    public void MagiDecide_Approval_ReceivesRefusedProposedOutcome()
    {
        var (_, _, _, approval, ctx) = Setup(new(LlmDecisionApprovalOutcome.Approved, Rationale: "accept refusal"));
        Execute(Step(), ctx);
        var cmd = Assert.Single(approval.Commands);
        Assert.Equal(LlmDecisionOutcome.Refused, cmd.ProposedOutcome);
        Assert.Equal("unsafe objective", cmd.ProposedRefusalReason);
        Assert.Equal("request human arbitration", cmd.ProposedAlternative);
        Assert.Equal("join", cmd.ProposedOptionId);
    }

    [Fact]
    public void MagiDecide_Approval_ApprovedRefusal_CommitsRefusalNoChosen()
    {
        var (_, _, _, _, ctx) = Setup(new(LlmDecisionApprovalOutcome.Approved, Rationale: "accept refusal"));
        Execute(Step(), ctx);
        AssertNoChosenOutputs(ctx);
        Assert.Equal("unsafe objective", ctx.Bb.GetOrDefault(RefusalKey, ""));
        Assert.Equal("request human arbitration", ctx.Bb.GetOrDefault(ProposalKey, ""));
        Assert.True(ctx.Bb.TryGet(ResultJsonKey, out _));
    }

    [Fact]
    public void MagiDecide_Approval_ApprovedRefusal_ResultJsonIncludesModelRefusalAndHumanRationale()
    {
        var (_, _, _, _, ctx) = Setup(new(LlmDecisionApprovalOutcome.Approved, Rationale: "human accepted", ApprovedBy: "user-7"));
        Execute(Step(), ctx);
        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        var root = doc.RootElement;
        Assert.Equal("refused", root.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("chosenOptionId").ValueKind);
        Assert.Equal("unsafe objective", root.GetProperty("refusal").GetProperty("reason").GetString());
        Assert.Equal("approved", root.GetProperty("approval").GetProperty("outcome").GetString());
        Assert.Equal("refused", root.GetProperty("approval").GetProperty("proposedOutcome").GetString());
        Assert.Equal("human accepted", root.GetProperty("approval").GetProperty("rationale").GetString());
        Assert.Equal("user-7", root.GetProperty("approval").GetProperty("approvedBy").GetString());
    }

    [Fact]
    public void MagiDecide_Approval_ChangedFromRefusal_CommitsHumanClosedOption()
    {
        var (_, _, _, _, ctx) = Setup(new(LlmDecisionApprovalOutcome.Changed, "mediate", "human override"));
        Execute(Step(), ctx);
        Assert.Equal("mediate", ctx.Bb.GetOrDefault(ChosenKey, ""));
        Assert.Equal("human override", ctx.Bb.GetOrDefault(RationaleKey, ""));
        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        var root = doc.RootElement;
        Assert.Equal("chosen", root.GetProperty("outcome").GetString());
        Assert.Equal("refused", root.GetProperty("modelOutcome").GetString());
        Assert.Equal("unsafe objective", root.GetProperty("modelRefusal").GetProperty("reason").GetString());
        Assert.Equal("mediate", root.GetProperty("chosenOptionId").GetString());
    }

    [Fact]
    public void MagiDecide_Approval_ChangedFromRefusal_RequiresHumanRationale()
    {
        var (_, _, _, _, ctx) = Setup(new(LlmDecisionApprovalOutcome.Changed, "mediate", " "));
        Assert.Throws<InvalidOperationException>(() => Execute(Step(), ctx));
        AssertNoStores(ctx);
    }

    [Fact]
    public void MagiDecide_Approval_OverrideRefusalIncludesApprovedBy_WhenProvided()
    {
        var (_, _, _, _, ctx) = Setup(new(LlmDecisionApprovalOutcome.Changed, "mediate", "human override", "human-9"));
        Execute(Step(), ctx);
        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        Assert.Equal("human-9", doc.RootElement.GetProperty("approval").GetProperty("approvedBy").GetString());
    }

    [Fact]
    public void MagiDecide_Approval_ChangedFromRefusal_InvalidClosedOption_FailsNoStore()
    {
        var (_, _, _, _, ctx) = Setup(new(LlmDecisionApprovalOutcome.Changed, "bogus", "override"));
        Assert.Throws<InvalidOperationException>(() => Execute(Step(), ctx));
        AssertNoStores(ctx);
    }

    [Fact]
    public void MagiDecide_Approval_RejectedRefusal_StoresNoOutputs()
    {
        var (_, _, _, _, ctx) = Setup(new(LlmDecisionApprovalOutcome.Rejected, Rationale: "no"));
        Assert.Throws<InvalidOperationException>(() => Execute(Step(), ctx));
        AssertNoStores(ctx);
    }


    [Fact]
    public void MagiDecide_RefusedReplay_WithApproval_StillDispatchesApprovalForFirstCommit()
    {
        var (a, b, j, approval, ctx) = Setup(new(LlmDecisionApprovalOutcome.Approved, Rationale: "accept refusal"));

        Execute(Step(), ctx);
        Assert.Single(approval.Commands);
        Assert.Equal(0, a.CallCount);
        Assert.Equal(0, b.CallCount);
        Assert.Equal(0, j.CallCount);

        Execute(Step(), ctx);
        Assert.Single(approval.Commands);
        Assert.Equal(0, a.CallCount);
        Assert.Equal(0, b.CallCount);
        Assert.Equal(0, j.CallCount);
    }

    [Fact]
    public void MagiDecide_Approval_ApprovedRefusal_ReentryDoesNotRedispatchMagiOrApproval()
    {
        var (a, b, j, approval, ctx) = Setup(new(LlmDecisionApprovalOutcome.Approved, Rationale: "ok"));
        Execute(Step(), ctx);
        ctx.Bb.Set(RefusalKey, string.Empty);
        ctx.Bb.Set(ResultJsonKey, string.Empty);
        Execute(Step(), ctx);
        Assert.Equal(0, a.CallCount);
        Assert.Equal(0, b.CallCount);
        Assert.Equal(0, j.CallCount);
        Assert.Single(approval.Commands);
        Assert.Equal("unsafe objective", ctx.Bb.GetOrDefault(RefusalKey, ""));
        Assert.False(ctx.Bb.TryGet(ChosenKey, out _));
    }

    private static AiStep Step() { var p = Participants(); return Llm.MagiDecide("magi.refusal.approval","intent","persona",b=>b.Add("k","v"),Options(),p.A,p.B,p.J,ChosenKey,RationaleKey,ResultJsonKey,new LlmMagiApprovalPolicy(), new LlmMagiRefusalPolicy(AllowProposedAlternative: true, StoreRefusalReasonAs: RefusalKey, StoreProposedAlternativeAs: ProposalKey)); }

    private static (FakeLlmDecisionClient A, FakeLlmDecisionClient B, FakeLlmMagiJudgeClient J, FakeMagiApprovalHandler Approval, AiCtx Ctx) Setup(LlmMagiApprovalResult approvalResult)
    {
        var p = Participants();
        var request = new LlmMagiRequest("magi.refusal.approval","intent","persona","{\"k\":\"v\"}",Options(),p.A,p.B,p.J,true,500,700,LlmMagiRequest.DefaultPromptTemplateVersion,LlmMagiRequest.DefaultOutputContractVersion);
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);
        var aRes = new LlmDecisionResult(LlmDecisionRequestHasher.ComputeHash(aReq), [new("join",0.9,1,"a1"),new("mediate",0.4,2,"a2"),new("refuse",0.2,3,"a3")], "a");
        var bRes = new LlmDecisionResult(LlmDecisionRequestHasher.ComputeHash(bReq), [new("mediate",0.9,1,"b1"),new("join",0.5,2,"b2"),new("refuse",0.2,3,"b3")], "b");
        var a = new FakeLlmDecisionClient(aRes);
        var b = new FakeLlmDecisionClient(bRes);
        var j = new FakeLlmMagiJudgeClient(new LlmMagiJudgment(null, p.A.Id, "judge refusal", LlmDecisionOutcome.Refused, new("unsafe objective", "request human arbitration")));
        var cassette = new InMemoryLlmMagiCassette();
        cassette.Put(LlmMagiRequestHasher.ComputeHash(request), request, new LlmMagiDecisionResult(LlmMagiRequestHasher.ComputeHash(request), request.AdvocateA, request.AdvocateB, request.Judge, aRes, bRes, new LlmMagiJudgment(null, p.A.Id, "judge refusal", LlmDecisionOutcome.Refused, new("unsafe objective", "request human arbitration")), LlmDecisionOutcome.Refused, new("unsafe objective", "request human arbitration")));
        var approval = new FakeMagiApprovalHandler(ActuatorHost.HandlerResult.CompletedWithPayload(approvalResult));
        var host = new ActuatorHost();
        host.Register<LlmMagiRequest>(new LlmMagiDecisionHandler(a,b,j,cassette,LlmCassetteMode.Replay));
        host.Register<LlmMagiApprovalCommand>(approval);
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = _ => Empty() });
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator, new LiveWorldBb(world.Bb));
        return (a,b,j,approval,ctx);
    }

    private static void AssertNoStores(AiCtx ctx)
    {
        Assert.False(ctx.Bb.TryGet(ChosenKey, out _));
        Assert.False(ctx.Bb.TryGet(RationaleKey, out _));
        Assert.False(ctx.Bb.TryGet(ResultJsonKey, out _));
        Assert.False(ctx.Bb.TryGet(RefusalKey, out _));
        Assert.False(ctx.Bb.TryGet(ProposalKey, out _));
    }

    private static void AssertNoChosenOutputs(AiCtx ctx)
    {
        Assert.False(ctx.Bb.TryGet(ChosenKey, out _));
        Assert.False(ctx.Bb.TryGet(RationaleKey, out _));
    }

    private static void Execute(AiStep step, AiCtx ctx) { var cursor = default(EventCursor); for (var i=0;i<8;i++) if (((IWaitEvent)step).TryConsume(ctx, ref cursor)) return; throw new TimeoutException(); }
    private static (LlmMagiParticipant A, LlmMagiParticipant B, LlmMagiParticipant J) Participants() => (Llm.MagiParticipant("advA","openai","gpt-5","a"), Llm.MagiParticipant("advB","anthropic","claude","b"), Llm.MagiParticipant("judge","gemini","g3","j"));
    private static IReadOnlyList<LlmDecisionOption> Options() => [Llm.Option("join","Join"),Llm.Option("mediate","Mediate"),Llm.Option("refuse","Refuse")];
    private static IEnumerator<AiStep> Empty() { yield break; }

    private sealed class FakeMagiApprovalHandler(ActuatorHost.HandlerResult response) : IActuationHandler<LlmMagiApprovalCommand>
    {
        public List<LlmMagiApprovalCommand> Commands { get; } = [];
        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, LlmMagiApprovalCommand command) { Commands.Add(command); return response; }
    }
}
