using System.Text.Json;
using System.Threading;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmMagiApprovalTests
{
    private static readonly BbKey<string> ChosenKey = new("magi.approval.chosen");
    private static readonly BbKey<string> RationaleKey = new("magi.approval.rationale");
    private static readonly BbKey<string> ResultJsonKey = new("magi.approval.result");

    [Fact]
    public void MagiApprovalPolicy_DefaultRequired_HasExpectedShape()
    {
        Assert.True(new LlmMagiApprovalPolicy().RequireApproval);
    }

    [Fact]
    public void MagiDecide_WithApproval_Approved_StoresHumanRationale()
    {
        var (_, _, _, approval, ctx) = Setup(new LlmMagiApprovalResult(LlmDecisionApprovalOutcome.Approved, Rationale: "human approved"));
        ExecuteStep(CreateStep(new LlmMagiApprovalPolicy()), ctx);
        Assert.Equal("join", ctx.Bb.GetOrDefault(ChosenKey, ""));
        Assert.Equal("human approved", ctx.Bb.GetOrDefault(RationaleKey, ""));
        Assert.Single(approval.Commands);
    }

    [Fact]
    public void MagiDecide_WithApproval_Changed_StoresHumanChosenClosedOption()
    {
        var (_, _, _, _, ctx) = Setup(new LlmMagiApprovalResult(LlmDecisionApprovalOutcome.Changed, "mediate", "human change"));
        ExecuteStep(CreateStep(new LlmMagiApprovalPolicy()), ctx);
        Assert.Equal("mediate", ctx.Bb.GetOrDefault(ChosenKey, ""));
    }

    [Fact]
    public void MagiDecide_WithApproval_Rejected_FailsAndDoesNotStoreOutputs()
    {
        var (_, _, _, _, ctx) = Setup(new LlmMagiApprovalResult(LlmDecisionApprovalOutcome.Rejected, Rationale: "no"));
        Assert.Throws<InvalidOperationException>(() => ExecuteStep(CreateStep(new LlmMagiApprovalPolicy()), ctx));
        Assert.False(ctx.Bb.TryGet(ChosenKey, out _));
        Assert.False(ctx.Bb.TryGet(RationaleKey, out _));
        Assert.False(ctx.Bb.TryGet(ResultJsonKey, out _));
    }

    [Fact]
    public void MagiDecide_WithApproval_ResultJsonIncludesApprovedBy_WhenProvided()
    {
        var (_, _, _, _, ctx) = Setup(new LlmMagiApprovalResult(LlmDecisionApprovalOutcome.Approved, Rationale: "ok", ApprovedBy: "user-7"));
        ExecuteStep(CreateStep(new LlmMagiApprovalPolicy()), ctx);
        using var doc = JsonDocument.Parse(ctx.Bb.GetOrDefault(ResultJsonKey, ""));
        Assert.Equal("user-7", doc.RootElement.GetProperty("approval").GetProperty("approvedBy").GetString());
    }

    private static AiStep CreateStep(LlmMagiApprovalPolicy? approval) {
        var p = CreateParticipants();
        return Llm.MagiDecide("magi.approval","intent","persona",b=>b.Add("k","v"),CreateOptions(),p.A,p.B,p.J,ChosenKey,RationaleKey,ResultJsonKey,approval);
    }

    private static (FakeLlmDecisionClient A, FakeLlmDecisionClient B, FakeLlmMagiJudgeClient J, FakeMagiApprovalHandler Approval, AiCtx Ctx) Setup(LlmMagiApprovalResult approvalResult)
    {
        var p = CreateParticipants();
        var request = new LlmMagiRequest("magi.approval","intent","persona","{\"k\":\"v\"}",CreateOptions(),p.A,p.B,p.J,LlmMagiRequest.DefaultPromptTemplateVersion,LlmMagiRequest.DefaultOutputContractVersion);
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);
        var a = new FakeLlmDecisionClient(new LlmDecisionResult(LlmDecisionRequestHasher.ComputeHash(aReq), [new("join",0.9,1,"r"),new("mediate",0.5,2,"r"),new("refuse",0.2,3,"r")], "a"));
        var b = new FakeLlmDecisionClient(new LlmDecisionResult(LlmDecisionRequestHasher.ComputeHash(bReq), [new("mediate",0.9,1,"r"),new("join",0.5,2,"r"),new("refuse",0.2,3,"r")], "b"));
        var j = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", p.A.Id, "judge rationale"));
        var approval = new FakeMagiApprovalHandler(approvalResult);

        var host = new ActuatorHost();
        host.Register<LlmMagiRequest>(new LlmMagiDecisionHandler(a,b,j,new InMemoryLlmMagiCassette(),LlmCassetteMode.Live));
        host.Register<LlmMagiApprovalCommand>(approval);
        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = _ => Empty() });
        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);
        return (a,b,j,approval,ctx);
    }

    private static (LlmMagiParticipant A, LlmMagiParticipant B, LlmMagiParticipant J) CreateParticipants() =>
        (Llm.MagiParticipant("advA","openai","gpt-5","pro"), Llm.MagiParticipant("advB","anthropic","claude","con"), Llm.MagiParticipant("judge","gemini","g3","judge"));
    private static IReadOnlyList<LlmDecisionOption> CreateOptions() => [Llm.Option("join","Join"),Llm.Option("mediate","Mediate"),Llm.Option("refuse","Refuse")];
    private static IEnumerator<AiStep> Empty() { yield break; }
    private static void ExecuteStep(AiStep step, AiCtx ctx) { var cursor = default(EventCursor); for (var i=0;i<8;i++) if (((IWaitEvent)step).TryConsume(ctx, ref cursor)) return; throw new TimeoutException(); }

    private sealed class FakeMagiApprovalHandler(LlmMagiApprovalResult result) : IActuationHandler<LlmMagiApprovalCommand>
    {
        public List<LlmMagiApprovalCommand> Commands { get; } = [];
        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, LlmMagiApprovalCommand command)
        {
            Commands.Add(command);
            return ActuatorHost.HandlerResult.CompletedWithPayload(result);
        }
    }
}
