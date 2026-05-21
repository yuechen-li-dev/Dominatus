using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.SemanticKernel.Tests;

public sealed class SemanticKernelCapabilityProfileTests
{
    [Fact]
    public void CapabilityProfile_RejectsMissingId()
        => Assert.Throws<ArgumentException>(() => new SemanticKernelCapabilityProfile(" ", "title", [new("graph.mail", "list_headers", SemanticKernelCapabilityRisk.Read)]));

    [Fact]
    public void CapabilityProfile_RejectsMissingTitle()
        => Assert.Throws<ArgumentException>(() => new SemanticKernelCapabilityProfile("graph", " ", [new("graph.mail", "list_headers", SemanticKernelCapabilityRisk.Read)]));

    [Fact]
    public void CapabilityProfile_RejectsNoEntries()
        => Assert.Throws<ArgumentException>(() => new SemanticKernelCapabilityProfile("graph", "title", []));

    [Fact]
    public void CapabilityProfile_RejectsDuplicateEntriesCaseInsensitive()
        => Assert.Throws<ArgumentException>(() => new SemanticKernelCapabilityProfile("graph", "title", [
            new("graph.mail", "list_headers", SemanticKernelCapabilityRisk.Read),
            new("GRAPH.MAIL", "LIST_HEADERS", SemanticKernelCapabilityRisk.Read)]));

    [Fact]
    public void CapabilityProfileEntry_RejectsMissingPluginName()
        => Assert.Throws<ArgumentException>(() => new SemanticKernelCapabilityProfileEntry("", "list_headers", SemanticKernelCapabilityRisk.Read));

    [Fact]
    public void CapabilityProfileEntry_RejectsMissingFunctionName()
        => Assert.Throws<ArgumentException>(() => new SemanticKernelCapabilityProfileEntry("graph.mail", "", SemanticKernelCapabilityRisk.Read));

    [Fact]
    public void CapabilityProfile_ToAllowedFunctions_ReturnsAllEntries()
    {
        var profile = CreateGraphLikeProfile();
        var allowed = profile.ToAllowedFunctions();
        Assert.Equal(profile.Entries.Count, allowed.Count);
    }

    [Fact]
    public void CapabilityProfile_ToAllowedFunctions_CanFilterReadOnly()
    {
        var profile = CreateGraphLikeProfile();
        var allowed = profile.ToAllowedFunctions(SemanticKernelCapabilityProfilePredicates.IsReadOnly);
        Assert.Equal(4, allowed.Count);
        Assert.DoesNotContain(allowed, x => x.FunctionName == "send_message");
    }

    [Fact]
    public void CapabilityProfile_ToAllowedFunctions_CanFilterApprovalRequired()
    {
        var profile = CreateGraphLikeProfile();
        var allowed = profile.ToAllowedFunctions(SemanticKernelCapabilityProfilePredicates.RequiresApproval);
        Assert.Equal(4, allowed.Count);
    }

    [Fact]
    public void CapabilityProfile_ToAllowedFunctions_PreservesOrder()
    {
        var profile = CreateGraphLikeProfile();
        var allowed = profile.ToAllowedFunctions();

        Assert.Collection(allowed,
            x => Assert.Equal(("graph.mail", "list_headers"), (x.PluginName, x.FunctionName)),
            x => Assert.Equal(("graph.mail", "read_message"), (x.PluginName, x.FunctionName)),
            x => Assert.Equal(("graph.mail", "send_message"), (x.PluginName, x.FunctionName)),
            x => Assert.Equal(("graph.calendar", "list_events"), (x.PluginName, x.FunctionName)),
            x => Assert.Equal(("graph.calendar", "create_event"), (x.PluginName, x.FunctionName)),
            x => Assert.Equal(("graph.todo", "list_tasks"), (x.PluginName, x.FunctionName)),
            x => Assert.Equal(("graph.todo", "create_task"), (x.PluginName, x.FunctionName)),
            x => Assert.Equal(("graph.files", "delete_file"), (x.PluginName, x.FunctionName)));
    }

    [Fact]
    public void CapabilityProfile_GraphLikeProfile_SeparatesReadAndWriteFunctions()
    {
        var profile = CreateGraphLikeProfile();

        var readOnly = profile.ToAllowedFunctions(SemanticKernelCapabilityProfilePredicates.IsReadOnly);
        var writeOrEffect = profile.Entries.Where(SemanticKernelCapabilityProfilePredicates.IsWriteOrEffectful).ToArray();

        Assert.Equal(4, readOnly.Count);
        Assert.Equal(4, writeOrEffect.Length);
    }

    [Fact]
    public void CapabilityProfile_GraphLikeProfile_DestructiveRequiresApproval()
    {
        var profile = CreateGraphLikeProfile();
        var destructive = Assert.Single(profile.Entries.Where(e => e.Risk == SemanticKernelCapabilityRisk.Destructive));
        Assert.True(destructive.RequiresHumanApproval);
    }

    [Fact]
    public void SemanticKernelCapabilityProfileSmoke_ReadFunctionInvokesThroughActuator()
    {
        var invoker = new RecordingInvoker((_, _, _, _) => Task.FromResult("ok"));
        var readOnlyProfile = CreateGraphLikeProfile();
        var handler = new SemanticKernelActuationHandler(invoker, new SemanticKernelActuatorOptions { AllowedFunctions = readOnlyProfile.ToAllowedFunctions(SemanticKernelCapabilityProfilePredicates.IsReadOnly) });
        var host = new ActuatorHost();
        host.Register<SemanticKernelFunctionCommand>(handler);

        var result = host.Dispatch(MakeCtx(host), new SemanticKernelFunctionCommand("graph.mail", "list_headers", "{}"));

        Assert.True(result.Ok);
        Assert.Single(invoker.Calls);
    }

    [Fact]
    public void SemanticKernelCapabilityProfileSmoke_WriteFunctionNotInReadAllowlist_IsDeniedBeforeInvocation()
    {
        var invoker = new RecordingInvoker((_, _, _, _) => Task.FromResult("should not run"));
        var readOnlyProfile = CreateGraphLikeProfile();
        var handler = new SemanticKernelActuationHandler(invoker, new SemanticKernelActuatorOptions { AllowedFunctions = readOnlyProfile.ToAllowedFunctions(SemanticKernelCapabilityProfilePredicates.IsReadOnly) });
        var host = new ActuatorHost();
        host.Register<SemanticKernelFunctionCommand>(handler);

        var result = host.Dispatch(MakeCtx(host), new SemanticKernelFunctionCommand("graph.mail", "send_message", "{}"));

        Assert.False(result.Ok);
        Assert.Equal("Semantic Kernel function is not allowlisted.", result.Error);
        Assert.Empty(invoker.Calls);
    }

    [Fact]
    public void SemanticKernelCapabilityProfileSmoke_UnprofiledDiscoveredFunction_NotAutoAllowed()
    {
        var profile = new SemanticKernelCapabilityProfile("graph-lite", "graph lite", [new("graph.mail", "list_headers", SemanticKernelCapabilityRisk.Read)]);
        var options = new SemanticKernelActuatorOptions { AllowedFunctions = profile.ToAllowedFunctions() };
        var catalog = new SemanticKernelFunctionCatalog(new FakeMetadataReader(new Dictionary<(string Plugin, string Function), SemanticKernelResolvedFunctionMetadata?>
        {
            [("graph.mail", "list_headers")] = new("Read headers", []),
            [("graph.mail", "secret_admin")] = new("Do privileged thing", [])
        }), options);

        var listed = catalog.GetAllowedFunctions();

        Assert.Single(listed);
        Assert.DoesNotContain(listed, x => x.FunctionName == "secret_admin");
    }

    private static SemanticKernelCapabilityProfile CreateGraphLikeProfile() => new(
        "graph.personal-assistant",
        "Graph personal assistant",
        [
            new("graph.mail", "list_headers", SemanticKernelCapabilityRisk.Read),
            new("graph.mail", "read_message", SemanticKernelCapabilityRisk.Read),
            new("graph.mail", "send_message", SemanticKernelCapabilityRisk.ExternalEffect, RequiresHumanApproval: true),
            new("graph.calendar", "list_events", SemanticKernelCapabilityRisk.Read),
            new("graph.calendar", "create_event", SemanticKernelCapabilityRisk.Write, RequiresHumanApproval: true),
            new("graph.todo", "list_tasks", SemanticKernelCapabilityRisk.Read),
            new("graph.todo", "create_task", SemanticKernelCapabilityRisk.Write, RequiresHumanApproval: true),
            new("graph.files", "delete_file", SemanticKernelCapabilityRisk.Destructive, RequiresHumanApproval: true)
        ]);

    private static AiCtx MakeCtx(ActuatorHost host)
    {
        var world = new AiWorld(host);
        var agent = new AiAgent(MakeBareBrain());
        world.Add(agent);
        return new AiCtx(world, agent, agent.Events, default, world.View, world.Mail, host);
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

    private sealed class FakeMetadataReader(IReadOnlyDictionary<(string Plugin, string Function), SemanticKernelResolvedFunctionMetadata?> byFunction) : ISemanticKernelFunctionMetadataReader
    {
        public bool TryGetMetadata(string pluginName, string functionName, out SemanticKernelResolvedFunctionMetadata? metadata)
        {
            if (byFunction.TryGetValue((pluginName, functionName), out var match))
            {
                metadata = match;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}
