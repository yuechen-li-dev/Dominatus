using Dominatus.Core;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.SemanticKernel.Tests;

public sealed class SemanticKernelCapabilityProfilePolicyIntegrationTests
{
    [Fact]
    public void SemanticKernelProfilePolicy_ReadFunctionAllowedWhenPolicyScorePasses()
    {
        var invoker = new RecordingInvoker((_, _, _, _) => Task.FromResult("ok"));
        var host = CreateHost(invoker, entry => entry.Risk == SemanticKernelCapabilityRisk.Read);
        host.AddPolicy(ActuationPolicies.ForCommand<SemanticKernelFunctionCommand>(Consideration.Constant(0.9f), threshold: 0.75f));

        var result = host.Dispatch(MakeCtx(host), new SemanticKernelFunctionCommand("graph.mail", "list_headers", "{}"));

        Assert.True(result.Ok);
        Assert.Single(invoker.Calls);
    }

    [Fact]
    public void SemanticKernelProfilePolicy_ReadFunctionDeniedWhenPolicyScoreFailsBeforeInvocation()
    {
        var invoker = new RecordingInvoker((_, _, _, _) => Task.FromResult("should not run"));
        var host = CreateHost(invoker, entry => entry.Risk == SemanticKernelCapabilityRisk.Read);
        host.AddPolicy(ActuationPolicies.ForCommand<SemanticKernelFunctionCommand>(Consideration.Constant(0.1f), threshold: 0.75f, reason: "Graph access denied."));

        var result = host.Dispatch(MakeCtx(host), new SemanticKernelFunctionCommand("graph.mail", "list_headers", "{}"));

        Assert.False(result.Ok);
        Assert.Equal("Graph access denied.", result.Error);
        Assert.Empty(invoker.Calls);
    }

    [Fact]
    public void SemanticKernelProfilePolicy_AllOfComposesAllowlistAndUtilityGate()
    {
        var invoker = new RecordingInvoker((_, _, _, _) => Task.FromResult("ok"));
        var host = CreateHost(invoker, entry => entry.Risk == SemanticKernelCapabilityRisk.Read);
        host.AddPolicy(ActuationPolicies.AllOf(
            ActuationPolicies.ForCommand<SemanticKernelFunctionCommand>(Consideration.Constant(1f), threshold: 0.75f),
            ActuationPolicies.Predicate((_, command) => command is SemanticKernelFunctionCommand sk && !sk.FunctionName.Contains("delete", StringComparison.OrdinalIgnoreCase), "Delete operations are blocked.")));

        var result = host.Dispatch(MakeCtx(host), new SemanticKernelFunctionCommand("graph.mail", "list_headers", "{}"));

        Assert.True(result.Ok);
        Assert.Single(invoker.Calls);
    }

    [Fact]
    public void SemanticKernelProfilePolicy_WriteFunctionStillDeniedWhenNotInAllowlistEvenIfPolicyAllows()
    {
        var invoker = new RecordingInvoker((_, _, _, _) => Task.FromResult("should not run"));
        var host = CreateHost(invoker, entry => entry.Risk == SemanticKernelCapabilityRisk.Read);
        host.AddPolicy(ActuationPolicies.ForCommand<SemanticKernelFunctionCommand>(Consideration.Constant(1f), threshold: 0.75f));

        var result = host.Dispatch(MakeCtx(host), new SemanticKernelFunctionCommand("graph.mail", "send_message", "{}"));

        Assert.False(result.Ok);
        Assert.Equal("Semantic Kernel function is not allowlisted.", result.Error);
        Assert.Empty(invoker.Calls);
    }

    private static ActuatorHost CreateHost(RecordingInvoker invoker, Func<SemanticKernelCapabilityProfileEntry, bool> allowPredicate)
    {
        var host = new ActuatorHost();
        var profile = CreateGraphLikeProfile();
        var options = new SemanticKernelActuatorOptions { AllowedFunctions = profile.ToAllowedFunctions(allowPredicate) };
        host.Register<SemanticKernelFunctionCommand>(new SemanticKernelActuationHandler(invoker, options));
        return host;
    }

    private static SemanticKernelCapabilityProfile CreateGraphLikeProfile() => new(
        "graph.personal-assistant",
        "Graph personal assistant",
        [
            new("graph.mail", "list_headers", SemanticKernelCapabilityRisk.Read),
            new("graph.mail", "send_message", SemanticKernelCapabilityRisk.ExternalEffect, RequiresHumanApproval: true)
        ]);

    private static AiCtx MakeCtx(ActuatorHost host)
    {
        var world = new AiWorld(host);
        var agent = new AiAgent(MakeBareBrain());
        world.Add(agent);
        return new AiCtx(world, agent, agent.Events, default, world.View, world.Mail, host, new LiveWorldBb(world.Bb));
    }

    private static HfsmInstance MakeBareBrain()
    {
        var g = new HfsmGraph { Root = new StateId("root") };
        g.Add(new StateId("root"), static _ => Empty());
        return new HfsmInstance(g);
    }

    private static IEnumerator<AiStep> Empty() { yield break; }

    private sealed class RecordingInvoker(Func<string, string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<string>> impl) : ISemanticKernelFunctionInvoker
    {
        public List<(string PluginName, string FunctionName)> Calls { get; } = [];

        public Task<string> InvokeAsync(string pluginName, string functionName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
        {
            Calls.Add((pluginName, functionName));
            return impl(pluginName, functionName, arguments, cancellationToken);
        }
    }
}
