using CoreActuationPolicies = Dominatus.Core.Runtime.ActuationPolicies;
using OptFlowActuationPolicies = Dominatus.OptFlow.ActuationPolicies;
using Dominatus.Core.Decision;
using Dominatus.Core.Runtime;

namespace Dominatus.Core.Tests;

public sealed class ActuationPolicyTests
{
    private record BlockedCommand : IActuationCommand;
    private sealed record OtherCommand : IActuationCommand;
    private sealed record DerivedBlockedCommand : BlockedCommand;

    private static AiCtx CreateCtx()
    {
        var host = new ActuatorHost();
        var world = new AiWorld(host);
        var agent = new AiAgent(TestGraphs.MakeBareBrain());
        world.Add(agent);
        return new AiCtx(world, agent, agent.Events, default, world.View, world.Mail, host);
    }

    [Fact]
    public void ActuationPolicies_When_AllowsWhenConsiderationMeetsThreshold()
    {
        var policy = CoreActuationPolicies.When(Consideration.Constant(0.75f), threshold: 0.5f);
        var decision = policy.Evaluate(CreateCtx(), new BlockedCommand());
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void ActuationPolicies_When_DeniesWhenConsiderationBelowThreshold()
    {
        var policy = CoreActuationPolicies.When(Consideration.Constant(0.1f), threshold: 0.5f);
        var decision = policy.Evaluate(CreateCtx(), new BlockedCommand());
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void ActuationPolicies_When_UsesClampedConsiderationScore()
    {
        var allowPolicy = CoreActuationPolicies.When(Consideration.Constant(2f), threshold: 1f);
        var denyPolicy = CoreActuationPolicies.When(Consideration.Constant(-1f), threshold: 0.1f);

        Assert.True(allowPolicy.Evaluate(CreateCtx(), new BlockedCommand()).Allowed);
        Assert.False(denyPolicy.Evaluate(CreateCtx(), new BlockedCommand()).Allowed);
    }

    [Fact]
    public void ActuationPolicies_When_DenialReasonIncludesCommandTypeAndThreshold()
    {
        var policy = CoreActuationPolicies.When(Consideration.Constant(0.25f), threshold: 0.5f);

        var decision = policy.Evaluate(CreateCtx(), new BlockedCommand());

        Assert.False(decision.Allowed);
        Assert.Contains(nameof(BlockedCommand), decision.Reason);
        Assert.Contains("0.5", decision.Reason);
    }

    [Fact]
    public void ActuationPolicies_ForCommand_AllowsOtherCommandTypes()
    {
        var policy = CoreActuationPolicies.ForCommand<BlockedCommand>(Consideration.Constant(0f), threshold: 0.5f);
        var decision = policy.Evaluate(CreateCtx(), new OtherCommand());
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void ActuationPolicies_ForCommand_AllowsMatchingCommandWhenScoreMeetsThreshold()
    {
        var policy = CoreActuationPolicies.ForCommand<BlockedCommand>(Consideration.Constant(0.8f), threshold: 0.5f);
        var decision = policy.Evaluate(CreateCtx(), new BlockedCommand());
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void ActuationPolicies_ForCommand_DeniesMatchingCommandWhenScoreBelowThreshold()
    {
        var policy = CoreActuationPolicies.ForCommand<BlockedCommand>(Consideration.Constant(0.1f), threshold: 0.5f);
        var decision = policy.Evaluate(CreateCtx(), new BlockedCommand());
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void ActuationPolicies_Score_AllowsWhenScoreMeetsThreshold()
    {
        var policy = CoreActuationPolicies.Score((_, _) => 0.7f, threshold: 0.5f);
        var decision = policy.Evaluate(CreateCtx(), new BlockedCommand());
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void ActuationPolicies_Score_DeniesWhenScoreBelowThreshold()
    {
        var policy = CoreActuationPolicies.Score((_, _) => 0.2f, threshold: 0.5f);
        var decision = policy.Evaluate(CreateCtx(), new BlockedCommand());
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void ActuationPolicies_Score_ClampsScore()
    {
        var allowPolicy = CoreActuationPolicies.Score((_, _) => 10f, threshold: 1f);
        var denyPolicy = CoreActuationPolicies.Score((_, _) => -10f, threshold: 0.1f);

        Assert.True(allowPolicy.Evaluate(CreateCtx(), new BlockedCommand()).Allowed);
        Assert.False(denyPolicy.Evaluate(CreateCtx(), new BlockedCommand()).Allowed);
    }

    [Fact]
    public void ActuationPolicies_Score_RejectsNullScorer()
    {
        Assert.Throws<ArgumentNullException>(() => CoreActuationPolicies.Score(null!));
    }

    [Fact]
    public void ActuationPolicies_Predicate_AllowsWhenTrue()
    {
        var policy = CoreActuationPolicies.Predicate((_, _) => true);
        var decision = policy.Evaluate(CreateCtx(), new BlockedCommand());
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void ActuationPolicies_Predicate_DeniesWhenFalse()
    {
        var policy = CoreActuationPolicies.Predicate((_, _) => false, "no");
        var decision = policy.Evaluate(CreateCtx(), new BlockedCommand());
        Assert.False(decision.Allowed);
        Assert.Equal("no", decision.Reason);
    }

    [Fact]
    public void ActuationPolicies_Predicate_RejectsNullPredicate()
    {
        Assert.Throws<ArgumentNullException>(() => CoreActuationPolicies.Predicate(null!));
    }

    [Fact]
    public void ActuationPolicies_BlockCommandTypes_BlocksListedCommand()
    {
        var policy = CoreActuationPolicies.BlockCommandTypes(typeof(BlockedCommand));
        var decision = policy.Evaluate(CreateCtx(), new BlockedCommand());
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void ActuationPolicies_BlockCommandTypes_AllowsUnlistedCommand()
    {
        var policy = CoreActuationPolicies.BlockCommandTypes(typeof(BlockedCommand));
        var decision = policy.Evaluate(CreateCtx(), new OtherCommand());
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void ActuationPolicies_BlockCommandTypes_RejectsNullType()
    {
        Assert.Throws<ArgumentException>(() => CoreActuationPolicies.BlockCommandTypes(typeof(BlockedCommand), null!));
    }

    [Fact]
    public void ActuationPolicies_BlockCommandTypes_RejectsNonCommandType()
    {
        Assert.Throws<ArgumentException>(() => CoreActuationPolicies.BlockCommandTypes(typeof(string)));
    }

    [Fact]
    public void ActuationPolicies_BlockCommandTypes_BlocksDerivedCommandViaAssignableFrom()
    {
        var policy = CoreActuationPolicies.BlockCommandTypes(typeof(BlockedCommand));
        var decision = policy.Evaluate(CreateCtx(), new DerivedBlockedCommand());
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void ActuationPolicies_AllOf_AllowsWhenAllAllow()
    {
        var policy = CoreActuationPolicies.AllOf(
            CoreActuationPolicies.AllowAll,
            CoreActuationPolicies.Predicate((_, _) => true));
        var decision = policy.Evaluate(CreateCtx(), new BlockedCommand());
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void ActuationPolicies_AllOf_FirstDenyWins()
    {
        var policy = CoreActuationPolicies.AllOf(
            CoreActuationPolicies.DenyAll("first"),
            CoreActuationPolicies.DenyAll("second"));
        var decision = policy.Evaluate(CreateCtx(), new BlockedCommand());
        Assert.False(decision.Allowed);
        Assert.Equal("first", decision.Reason);
    }

    [Fact]
    public void ActuationPolicies_AllOf_DoesNotEvaluatePoliciesAfterFirstDeny()
    {
        var secondEvaluated = false;
        var second = new TestPolicy((_, _) =>
        {
            secondEvaluated = true;
            return ActuationPolicyDecision.Allow();
        });

        var composite = CoreActuationPolicies.AllOf(
            CoreActuationPolicies.DenyAll("blocked"),
            second);

        var decision = composite.Evaluate(CreateCtx(), new BlockedCommand());

        Assert.False(decision.Allowed);
        Assert.False(secondEvaluated);
    }

    [Fact]
    public void ActuationPolicies_AllOf_RejectsNullPolicy()
    {
        Assert.Throws<ArgumentException>(() => CoreActuationPolicies.AllOf(CoreActuationPolicies.AllowAll, null!));
    }

    [Fact]
    public void OptFlowActuationPolicies_ForwardToCorePolicies()
    {
        var optFlowPolicy = new OptFlowActuationPolicies.BlockCommandTypes(typeof(BlockedCommand));
        var corePolicy = CoreActuationPolicies.BlockCommandTypes(typeof(BlockedCommand));

        var ctx = CreateCtx();
        var command = new DerivedBlockedCommand();
        var optFlowDecision = optFlowPolicy.Evaluate(ctx, command);
        var coreDecision = corePolicy.Evaluate(ctx, command);

        Assert.Equal(coreDecision.Allowed, optFlowDecision.Allowed);
    }

    private sealed class TestPolicy(Func<AiCtx, IActuationCommand, ActuationPolicyDecision> evaluator) : IActuationPolicy
    {
        public ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command) => evaluator(ctx, command);
    }
}
