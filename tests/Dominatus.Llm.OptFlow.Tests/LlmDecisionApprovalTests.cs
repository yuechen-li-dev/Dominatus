using System.Text.Json;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using System.Threading;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmDecisionApprovalTests
{
    private static readonly BbKey<string> ChosenKey = new("decision.chosen");
    private static readonly BbKey<string> RationaleKey = new("decision.rationale");
    private static readonly BbKey<string> ResultJsonKey = new("decision.resultJson");

    [Fact]
    public void LlmDecide_WithApproval_Approved_StoresProposedChoice()
    {
        var client = new FakeLlmDecisionClient(CreateResult("a",0.9,"b",0.5));
        var approval = new FakeApprovalHandler(new LlmDecisionApprovalResult(LlmDecisionApprovalOutcome.Approved, Rationale: "approved refusal"));
        var (_,ctx)=CreateWorldAndCtx(client,approval);

        ExecuteStep(CreateStep(new LlmDecisionApprovalPolicy()),ctx);

        Assert.Equal("a", ctx.Bb.GetOrDefault(ChosenKey, ""));
        Assert.Single(approval.Commands);
    }

    [Fact]
    public void LlmDecide_WithApproval_Changed_StoresHumanChosenClosedOption()
    {
        var client = new FakeLlmDecisionClient(CreateResult("a",0.9,"b",0.5));
        var approval = new FakeApprovalHandler(new LlmDecisionApprovalResult(LlmDecisionApprovalOutcome.Changed, "b", "human"));
        var (_,ctx)=CreateWorldAndCtx(client,approval);

        ExecuteStep(CreateStep(new LlmDecisionApprovalPolicy()),ctx);

        Assert.Equal("b", ctx.Bb.GetOrDefault(ChosenKey, ""));
        Assert.Equal("human", ctx.Bb.GetOrDefault(RationaleKey, ""));
    }

    [Fact]
    public void LlmDecide_WithApproval_Rejected_FailsAndDoesNotStoreOutputs()
    {
        var client = new FakeLlmDecisionClient(CreateResult("a",0.9,"b",0.5));
        var approval = new FakeApprovalHandler(new LlmDecisionApprovalResult(LlmDecisionApprovalOutcome.Rejected));
        var (_,ctx)=CreateWorldAndCtx(client,approval);

        Assert.Throws<InvalidOperationException>(() => ExecuteStep(CreateStep(new LlmDecisionApprovalPolicy()),ctx));
        Assert.False(ctx.Bb.TryGet(ChosenKey, out _));
    }

    [Fact]
    public void LlmDecide_WithApproval_ResultJsonIncludesApprovedBy_WhenProvided()
    {
        var client = new FakeLlmDecisionClient(CreateResult("a",0.9,"b",0.5));
        var approval = new FakeApprovalHandler(new LlmDecisionApprovalResult(LlmDecisionApprovalOutcome.Changed, "b", "human", "user-7"));
        var (_,ctx)=CreateWorldAndCtx(client,approval);

        ExecuteStep(CreateStep(new LlmDecisionApprovalPolicy()),ctx);

        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        Assert.Equal("user-7", doc.RootElement.GetProperty("approval").GetProperty("approvedBy").GetString());
    }

    [Fact]
    public void LlmDecide_WithApproval_ResultJsonIncludesApprovalMetadata()
    {
        var client = new FakeLlmDecisionClient(CreateResult("a",0.9,"b",0.5));
        var approval = new FakeApprovalHandler(new LlmDecisionApprovalResult(LlmDecisionApprovalOutcome.Changed, "b", "human"));
        var (_,ctx)=CreateWorldAndCtx(client,approval);

        ExecuteStep(CreateStep(new LlmDecisionApprovalPolicy()),ctx);

        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        Assert.Equal("changed", doc.RootElement.GetProperty("approval").GetProperty("outcome").GetString());
    }


    [Fact]
    public void LlmDecide_Approval_ReceivesRefusedProposedOutcome()
    {
        var client = new FakeLlmDecisionClient(CreateRefusedResult());
        var approval = new FakeApprovalHandler(new LlmDecisionApprovalResult(LlmDecisionApprovalOutcome.Approved, Rationale: "accept refusal"));
        var (_,ctx)=CreateWorldAndCtx(client,approval);

        ExecuteStep(CreateStep(new LlmDecisionApprovalPolicy(), new LlmDecisionRefusalPolicy(AllowProposedAlternative: true)),ctx);

        var cmd = Assert.Single(approval.Commands);
        Assert.Equal(LlmDecisionOutcome.Refused, cmd.ProposedOutcome);
        Assert.Equal("unsafe prompt", cmd.ProposedRefusalReason);
        Assert.Equal("ask for more context", cmd.ProposedAlternative);
        Assert.Equal("a", cmd.ProposedOptionId);
    }

    [Fact]
    public void LlmDecide_Approval_ApprovedRefusal_CommitsRefusalNoChosen()
    {
        var refusalKey = new BbKey<string>("decision.refusal");
        var client = new FakeLlmDecisionClient(CreateRefusedResult());
        var approval = new FakeApprovalHandler(new LlmDecisionApprovalResult(LlmDecisionApprovalOutcome.Approved, Rationale: "human accepted refusal"));
        var (_, ctx) = CreateWorldAndCtx(client, approval);
        var step = CreateStep(new LlmDecisionApprovalPolicy(), new LlmDecisionRefusalPolicy(StoreRefusalReasonAs: refusalKey, AllowProposedAlternative: true));

        ExecuteStep(step, ctx);

        Assert.Equal("unsafe prompt", ctx.Bb.GetOrDefault(refusalKey, ""));
        Assert.False(ctx.Bb.TryGet(ChosenKey, out _));
        Assert.False(ctx.Bb.TryGet(RationaleKey, out _));
        Assert.False(string.IsNullOrWhiteSpace(ctx.Bb.GetOrDefault(ResultJsonKey, "")));
    }

    [Fact]
    public void LlmDecide_Approval_ApprovedRefusal_ResultJsonIncludesModelRefusalAndHumanRationale()
    {
        var client = new FakeLlmDecisionClient(CreateRefusedResult());
        var approval = new FakeApprovalHandler(new LlmDecisionApprovalResult(LlmDecisionApprovalOutcome.Approved, Rationale: "approved by reviewer", ApprovedBy: "reviewer-2"));
        var (_, ctx) = CreateWorldAndCtx(client, approval);
        ExecuteStep(CreateStep(new LlmDecisionApprovalPolicy(), new LlmDecisionRefusalPolicy(AllowProposedAlternative: true)), ctx);

        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        var root = doc.RootElement;
        Assert.Equal("refused", root.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("chosenOptionId").ValueKind);
        Assert.Equal("unsafe prompt", root.GetProperty("refusal").GetProperty("reason").GetString());
        Assert.True(root.GetProperty("approval").GetProperty("required").GetBoolean());
        Assert.Equal("approved", root.GetProperty("approval").GetProperty("outcome").GetString());
        Assert.Equal("refused", root.GetProperty("approval").GetProperty("proposedOutcome").GetString());
        Assert.Equal("approved by reviewer", root.GetProperty("approval").GetProperty("rationale").GetString());
        Assert.Equal("reviewer-2", root.GetProperty("approval").GetProperty("approvedBy").GetString());
    }

    [Fact]
    public void LlmDecide_Approval_ChangedFromRefusal_CommitsHumanClosedOption()
    {
        var refusalKey = new BbKey<string>("decision.refusal");
        var client = new FakeLlmDecisionClient(CreateRefusedResult());
        var approval = new FakeApprovalHandler(new LlmDecisionApprovalResult(LlmDecisionApprovalOutcome.Changed, "b", "human override"));
        var (_, ctx) = CreateWorldAndCtx(client, approval);
        ExecuteStep(CreateStep(new LlmDecisionApprovalPolicy(), new LlmDecisionRefusalPolicy(StoreRefusalReasonAs: refusalKey, AllowProposedAlternative: true)), ctx);

        Assert.Equal("b", ctx.Bb.GetOrDefault(ChosenKey, ""));
        Assert.Equal("human override", ctx.Bb.GetOrDefault(RationaleKey, ""));
        Assert.False(ctx.Bb.TryGet(refusalKey, out _));
        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        Assert.Equal("refused", doc.RootElement.GetProperty("approval").GetProperty("proposedOutcome").GetString());
        Assert.Equal("changed", doc.RootElement.GetProperty("approval").GetProperty("outcome").GetString());
    }

    [Fact]
    public void LlmDecide_Approval_ChangedFromRefusal_RequiresHumanRationale()
    {
        var refusalKey = new BbKey<string>("decision.refusal");
        var client = new FakeLlmDecisionClient(CreateRefusedResult());
        var approval = new FakeApprovalHandler(new LlmDecisionApprovalResult(LlmDecisionApprovalOutcome.Changed, "b", " "));
        var (_, ctx) = CreateWorldAndCtx(client, approval);

        Assert.Throws<InvalidOperationException>(() => ExecuteStep(CreateStep(new LlmDecisionApprovalPolicy(), new LlmDecisionRefusalPolicy(StoreRefusalReasonAs: refusalKey, AllowProposedAlternative: true)), ctx));
        Assert.False(ctx.Bb.TryGet(ChosenKey, out _));
        Assert.False(ctx.Bb.TryGet(RationaleKey, out _));
        Assert.False(ctx.Bb.TryGet(ResultJsonKey, out _));
        Assert.False(ctx.Bb.TryGet(refusalKey, out _));
    }

    [Fact]
    public void LlmDecide_Approval_OverrideRefusalIncludesApprovedBy_WhenProvided()
    {
        var client = new FakeLlmDecisionClient(CreateRefusedResult());
        var approval = new FakeApprovalHandler(new LlmDecisionApprovalResult(LlmDecisionApprovalOutcome.Changed, "b", "human override", "user-9"));
        var (_, ctx) = CreateWorldAndCtx(client, approval);
        ExecuteStep(CreateStep(new LlmDecisionApprovalPolicy(), new LlmDecisionRefusalPolicy(AllowProposedAlternative: true)), ctx);

        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        Assert.Equal("user-9", doc.RootElement.GetProperty("approval").GetProperty("approvedBy").GetString());
    }

    [Fact]
    public void LlmDecide_Approval_RejectedRefusal_StoresNoOutputs()
    {
        var refusalKey = new BbKey<string>("decision.refusal");
        var proposalKey = new BbKey<string>("decision.proposal");
        var client = new FakeLlmDecisionClient(CreateRefusedResult());
        var approval = new FakeApprovalHandler(new LlmDecisionApprovalResult(LlmDecisionApprovalOutcome.Rejected, Rationale: "reject refusal"));
        var (_, ctx) = CreateWorldAndCtx(client, approval);

        Assert.Throws<InvalidOperationException>(() => ExecuteStep(CreateStep(new LlmDecisionApprovalPolicy(), new LlmDecisionRefusalPolicy(StoreRefusalReasonAs: refusalKey, StoreProposedAlternativeAs: proposalKey, AllowProposedAlternative: true)), ctx));
        Assert.False(ctx.Bb.TryGet(ChosenKey, out _));
        Assert.False(ctx.Bb.TryGet(RationaleKey, out _));
        Assert.False(ctx.Bb.TryGet(ResultJsonKey, out _));
        Assert.False(ctx.Bb.TryGet(refusalKey, out _));
        Assert.False(ctx.Bb.TryGet(proposalKey, out _));
    }

    private static AiStep CreateStep(LlmDecisionApprovalPolicy? approval = null, LlmDecisionRefusalPolicy? refusal = null) => Llm.Decide(
        "approval",
        "intent",
        "persona",
        b => b.Add("k", "v"),
        [Llm.Option("a", "A"), Llm.Option("b", "B")],
        ChosenKey,
        RationaleKey,
        ResultJsonKey,
        policy: new LlmDecisionPolicy(1,1,0.1),
        approval: approval,
        refusal: refusal);

    private static (AiWorld world, AiCtx ctx) CreateWorldAndCtx(ILlmDecisionClient client, FakeApprovalHandler approval)
    {
        var host = new ActuatorHost();
        host.Register<LlmDecisionRequest>(new LlmDecisionScoringHandler(client, new InMemoryLlmDecisionCassette(), LlmCassetteMode.Live));
        host.Register<LlmDecisionApprovalCommand>(approval);
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = _ => Empty() });
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator, new LiveWorldBb(world.Bb));
        return (world, ctx);
    }
    private static IEnumerator<AiStep> Empty() { yield break; }

    private static void ExecuteStep(AiStep step, AiCtx ctx)
    {
        var cursor = default(EventCursor);
        for (var i = 0; i < 8; i++)
        {
            if (((IWaitEvent)step).TryConsume(ctx, ref cursor)) return;
        }

        throw new TimeoutException();
    }

    private static LlmDecisionResult CreateResult(string firstId,double firstScore,string secondId,double secondScore)
    {
        var req = new LlmDecisionRequest("approval","intent","persona","{\"k\":\"v\"}",[Llm.Option("a","A"),Llm.Option("b","B")],Llm.DefaultSampling,LlmDecisionRequest.DefaultPromptTemplateVersion,LlmDecisionRequest.DefaultOutputContractVersion, false, LlmDecisionResult.MaxRationaleLength, 500);
        var hash = LlmDecisionRequestHasher.ComputeHash(req);
        return new LlmDecisionResult(hash,[new LlmDecisionOptionScore(firstId, firstScore,1,"r1"), new LlmDecisionOptionScore(secondId,secondScore,2,"r2")],"overall");
    }



    private static LlmDecisionResult CreateRefusedResult()
    {
        var req = new LlmDecisionRequest("approval","intent","persona","{\"k\":\"v\"}",[Llm.Option("a","A"),Llm.Option("b","B")],Llm.DefaultSampling,LlmDecisionRequest.DefaultPromptTemplateVersion,LlmDecisionRequest.DefaultOutputContractVersion, true, LlmDecisionResult.MaxRationaleLength, 500);
        var hash = LlmDecisionRequestHasher.ComputeHash(req);
        return new LlmDecisionResult(hash,[new LlmDecisionOptionScore("a", 0.4,1,"least bad"), new LlmDecisionOptionScore("b",0.1,2,"worse")],"model refused", LlmDecisionOutcome.Refused, new LlmDecisionRefusal("unsafe prompt", "ask for more context"));
    }

    private sealed class FakeApprovalHandler(LlmDecisionApprovalResult result) : IActuationHandler<LlmDecisionApprovalCommand>
    {
        public List<LlmDecisionApprovalCommand> Commands { get; } = [];
        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, LlmDecisionApprovalCommand command)
        {
            Commands.Add(command);
            return ActuatorHost.HandlerResult.CompletedWithPayload(result);
        }
    }
}
