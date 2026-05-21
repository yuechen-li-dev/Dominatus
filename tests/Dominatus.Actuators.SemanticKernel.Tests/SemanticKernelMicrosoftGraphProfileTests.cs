using Dominatus.Core;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.SemanticKernel.Tests;

public sealed class SemanticKernelMicrosoftGraphProfileTests
{
    [Fact]
    public void MicrosoftGraphProfile_OutlookMailCalendar_HasExpectedIdTitle()
    {
        var profile = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendar();
        Assert.Equal("microsoft-graph.outlook-mail-calendar", profile.Id);
        Assert.Equal("Microsoft Graph Outlook Mail/Calendar", profile.Title);
    }

    [Fact]
    public void MicrosoftGraphProfile_OutlookMailCalendar_HasMailAndCalendarEntries()
    {
        var entries = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendar().Entries;
        Assert.Contains(entries, e => e.PluginName == "graph.mail" && e.FunctionName == "list_messages");
        Assert.Contains(entries, e => e.PluginName == "graph.calendar" && e.FunctionName == "cancel_event");
        Assert.Equal(8, entries.Count);
    }

    [Fact]
    public void MicrosoftGraphProfile_OutlookMailCalendar_ClassifiesReadWriteExternalDestructive()
    {
        var entries = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendar().Entries;
        Assert.Equal(3, entries.Count(e => e.Risk == SemanticKernelCapabilityRisk.Read));
        Assert.Equal(3, entries.Count(e => e.Risk == SemanticKernelCapabilityRisk.Write));
        Assert.Single(entries.Where(e => e.Risk == SemanticKernelCapabilityRisk.ExternalEffect));
        Assert.Single(entries.Where(e => e.Risk == SemanticKernelCapabilityRisk.Destructive));
    }

    [Fact]
    public void MicrosoftGraphProfile_OutlookMailCalendar_MarksExternalAndDestructiveApprovalRequired()
    {
        var entries = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendar().Entries;
        Assert.True(entries.Single(e => e.FunctionName == "send_message").RequiresHumanApproval);
        Assert.True(entries.Single(e => e.FunctionName == "cancel_event").RequiresHumanApproval);
    }

    [Fact]
    public void MicrosoftGraphProfile_OutlookMailCalendar_CreateDraftIsWriteButNotExternalEffect()
    {
        var draft = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendar().Entries.Single(e => e.FunctionName == "create_draft");
        Assert.Equal(SemanticKernelCapabilityRisk.Write, draft.Risk);
        Assert.False(draft.RequiresHumanApproval);
    }

    [Fact]
    public void MicrosoftGraphProfile_OutlookMailCalendar_PluginPrefixIsApplied()
    {
        var entries = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendar("msgraph").Entries;
        Assert.All(entries, e => Assert.StartsWith("msgraph.", e.PluginName, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("graph mail")]
    public void MicrosoftGraphProfile_OutlookMailCalendar_RejectsInvalidPluginPrefix(string pluginPrefix)
        => Assert.Throws<ArgumentException>(() => SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendar(pluginPrefix));

    [Fact]
    public void MicrosoftGraphProfile_ReadAllowlist_ContainsOnlyReadFunctions()
    {
        var allowlist = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendarReadAllowlist();
        Assert.Equal(3, allowlist.Count);
        Assert.DoesNotContain(allowlist, a => a.FunctionName == "send_message");
    }

    [Fact]
    public void MicrosoftGraphProfile_WriteAllowlist_ContainsDraftAndCalendarWrites()
    {
        var allowlist = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendarWriteAllowlist();
        Assert.Equal(3, allowlist.Count);
        Assert.Contains(allowlist, x => x.PluginName == "graph.mail" && x.FunctionName == "create_draft");
        Assert.Contains(allowlist, x => x.PluginName == "graph.calendar" && x.FunctionName == "create_event");
        Assert.DoesNotContain(allowlist, x => x.FunctionName == "cancel_event");
    }

    [Fact]
    public void MicrosoftGraphProfile_ExternalEffectAllowlist_ContainsSendMail()
    {
        var allowlist = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendarExternalEffectAllowlist();
        var send = Assert.Single(allowlist);
        Assert.Equal(("graph.mail", "send_message"), (send.PluginName, send.FunctionName));
    }

    [Fact]
    public void MicrosoftGraphProfile_ToAllowedFunctions_PreservesProfileOrder()
    {
        var allowlist = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendar().ToAllowedFunctions();
        Assert.Collection(allowlist,
            x => Assert.Equal(("graph.mail", "list_messages"), (x.PluginName, x.FunctionName)),
            x => Assert.Equal(("graph.mail", "read_message"), (x.PluginName, x.FunctionName)),
            x => Assert.Equal(("graph.mail", "create_draft"), (x.PluginName, x.FunctionName)),
            x => Assert.Equal(("graph.mail", "send_message"), (x.PluginName, x.FunctionName)),
            x => Assert.Equal(("graph.calendar", "list_events"), (x.PluginName, x.FunctionName)),
            x => Assert.Equal(("graph.calendar", "create_event"), (x.PluginName, x.FunctionName)),
            x => Assert.Equal(("graph.calendar", "update_event"), (x.PluginName, x.FunctionName)),
            x => Assert.Equal(("graph.calendar", "cancel_event"), (x.PluginName, x.FunctionName)));
    }

    [Fact]
    public void MicrosoftGraphProfileSmoke_ReadMailAllowed_InvokesThroughSemanticKernelActuator()
    {
        var invoker = new RecordingInvoker((_, _, _, _) => Task.FromResult("ok"));
        var host = CreateHost(invoker, new SemanticKernelActuatorOptions { AllowedFunctions = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendarReadAllowlist() });
        var result = host.Dispatch(MakeCtx(host), new SemanticKernelFunctionCommand("graph.mail", "read_message", "{}"));
        Assert.True(result.Ok);
        Assert.Single(invoker.Calls);
    }

    [Fact]
    public void MicrosoftGraphProfileSmoke_SendMailExcludedFromReadAllowlist_DeniedBeforeInvocation()
    {
        var invoker = new RecordingInvoker((_, _, _, _) => Task.FromResult("should not run"));
        var host = CreateHost(invoker, new SemanticKernelActuatorOptions { AllowedFunctions = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendarReadAllowlist() });
        var result = host.Dispatch(MakeCtx(host), new SemanticKernelFunctionCommand("graph.mail", "send_message", "{}"));
        Assert.False(result.Ok);
        Assert.Empty(invoker.Calls);
    }

    [Fact]
    public void MicrosoftGraphProfileSmoke_CreateEventRequiresWriteAllowlist_OrIsDenied()
    {
        var invoker = new RecordingInvoker((_, _, _, _) => Task.FromResult("ok"));
        var readHost = CreateHost(invoker, new SemanticKernelActuatorOptions { AllowedFunctions = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendarReadAllowlist() });
        var denied = readHost.Dispatch(MakeCtx(readHost), new SemanticKernelFunctionCommand("graph.calendar", "create_event", "{}"));
        Assert.False(denied.Ok);

        var writeHost = CreateHost(invoker, new SemanticKernelActuatorOptions { AllowedFunctions = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendarWriteAllowlist() });
        var allowed = writeHost.Dispatch(MakeCtx(writeHost), new SemanticKernelFunctionCommand("graph.calendar", "create_event", "{}"));
        Assert.True(allowed.Ok);
    }

    [Fact]
    public void MicrosoftGraphProfileSmoke_CancelEventDestructive_NotInWriteAllowlistByDefault()
    {
        var invoker = new RecordingInvoker((_, _, _, _) => Task.FromResult("ok"));
        var host = CreateHost(invoker, new SemanticKernelActuatorOptions { AllowedFunctions = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendarWriteAllowlist() });
        var result = host.Dispatch(MakeCtx(host), new SemanticKernelFunctionCommand("graph.calendar", "cancel_event", "{}"));
        Assert.False(result.Ok);
        Assert.Empty(invoker.Calls);
    }

    [Fact]
    public void MicrosoftGraphProfilePolicy_SendMailDeniedWhenApprovalFlagMissing()
    {
        var approved = false;
        var invoker = new RecordingInvoker((_, _, _, _) => Task.FromResult("ok"));
        var host = CreateHost(invoker, new SemanticKernelActuatorOptions { AllowedFunctions = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendarExternalEffectAllowlist() });
        host.AddPolicy(ActuationPolicies.Predicate((_, command) => command is not SemanticKernelFunctionCommand sk || sk.FunctionName != "send_message" || approved, "Human approval is required."));

        var denied = host.Dispatch(MakeCtx(host), new SemanticKernelFunctionCommand("graph.mail", "send_message", "{}"));
        Assert.False(denied.Ok);
        Assert.Empty(invoker.Calls);
    }

    [Fact]
    public void MicrosoftGraphProfilePolicy_SendMailAllowedWhenApprovalFlagPresent()
    {
        var approved = true;
        var invoker = new RecordingInvoker((_, _, _, _) => Task.FromResult("ok"));
        var host = CreateHost(invoker, new SemanticKernelActuatorOptions { AllowedFunctions = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendarExternalEffectAllowlist() });
        host.AddPolicy(ActuationPolicies.Predicate((_, command) => command is not SemanticKernelFunctionCommand sk || sk.FunctionName != "send_message" || approved, "Human approval is required."));

        var allowed = host.Dispatch(MakeCtx(host), new SemanticKernelFunctionCommand("graph.mail", "send_message", "{}"));
        Assert.True(allowed.Ok);
        Assert.Single(invoker.Calls);
    }

    private static ActuatorHost CreateHost(ISemanticKernelFunctionInvoker invoker, SemanticKernelActuatorOptions options)
    {
        var host = new ActuatorHost();
        host.Register<SemanticKernelFunctionCommand>(new SemanticKernelActuationHandler(invoker, options));
        return host;
    }

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
}
