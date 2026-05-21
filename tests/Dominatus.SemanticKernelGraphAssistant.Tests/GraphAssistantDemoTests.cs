using System.Xml.Linq;
using Dominatus.Actuators.SemanticKernel;
using Dominatus.SemanticKernelGraphAssistant;

namespace Dominatus.SemanticKernelGraphAssistant.Tests;

public sealed class GraphAssistantDemoTests
{
    [Fact]
    public void GraphAssistant_Scheduling_NoApproval_UsesLlmCallToGenerateMeetingProposal()
    {
        var result = GraphAssistantDemo.Run(false, GraphAssistantScenario.SchedulingRequest);
        Assert.Equal(GraphAssistantScenario.SchedulingRequest, result.Scenario);
        Assert.False(result.ApprovalGranted);
        Assert.True(result.UsedLlmCall || result.LlmCallCount > 0);
        Assert.Contains("next Tuesday afternoon", result.MeetingProposalText ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.CreatedCalendarEvent);
        Assert.False(result.SentMail);
    }

    [Fact]
    public void GraphAssistant_Scheduling_NoApproval_DoesNotCreateEvent()
    {
        var result = GraphAssistantDemo.Run(false, GraphAssistantScenario.SchedulingRequest);
        Assert.DoesNotContain("graph.calendar.create_event", result.InvokedFunctions);
        Assert.False(result.CreatedCalendarEvent);
        Assert.Contains(result.DecisionEvents, e => e.Contains("Approval missing; calendar create not performed", StringComparison.Ordinal));
    }

    [Fact]
    public void GraphAssistant_Scheduling_WithApproval_CreatesCalendarEvent()
    {
        var result = GraphAssistantDemo.Run(true, GraphAssistantScenario.SchedulingRequest);
        Assert.True(result.ApprovalGranted);
        Assert.Equal(GraphAssistantScenario.SchedulingRequest, result.Scenario);
        Assert.True(result.CreatedCalendarEvent);
        Assert.Equal("event-created:meeting-next-week", result.CreatedEventId);
        Assert.Contains("graph.calendar.create_event", result.InvokedFunctions);
        Assert.Contains("next Tuesday afternoon", result.MeetingProposalText ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.SentMail);
    }

    [Fact]
    public void GraphAssistant_Scheduling_WithApproval_UsesAiDecide()
    {
        var result = GraphAssistantDemo.Run(true, GraphAssistantScenario.SchedulingRequest);
        Assert.Contains(result.DecisionEvents, e => e.Contains("Ai.Decide chose CreateApprovedCalendarEvent", StringComparison.Ordinal));
    }

    [Fact]
    public void GraphAssistant_CalendarCreatePolicyDeniesWithoutApprovalBeforeInvocation()
    {
        var result = GraphAssistantDemo.Run(false, GraphAssistantScenario.SchedulingRequest);
        Assert.DoesNotContain("graph.calendar.create_event", result.InvokedFunctions);
        Assert.DoesNotContain("graph.calendar.create_event", result.AllowedFunctions);
        Assert.False(result.CreatedCalendarEvent);
    }

[Fact]
    public void GraphAssistant_UrgentReply_BehaviorStillPasses()
    {
        var noApproval = GraphAssistantDemo.Run(false, GraphAssistantScenario.UrgentReply);
        var withApproval = GraphAssistantDemo.Run(true, GraphAssistantScenario.UrgentReply);
        Assert.True(noApproval.CreatedDraft);
        Assert.False(noApproval.SentMail);
        Assert.True(withApproval.SentMail);
    }

    [Fact]
    public void GraphAssistant_NoLiveLlmOrGraphDependencies()
    {
        var root = FindRepoRoot();
        var sampleProject = Path.Combine(root, "samples", "Dominatus.SemanticKernelGraphAssistant", "Dominatus.SemanticKernelGraphAssistant.csproj");
        var doc = XDocument.Load(sampleProject);
        var text = doc.ToString();
        Assert.DoesNotContain("Microsoft.Graph", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Azure.Identity", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MSAL", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenAI", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Anthropic", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Azure.AI.OpenAI", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mcp", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Planner", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Agents", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GraphAssistant_CompletesWithinMaxTicks_BothScenarios()
    {
        var urgent = GraphAssistantDemo.Run(false, GraphAssistantScenario.UrgentReply);
        var scheduling = GraphAssistantDemo.Run(false, GraphAssistantScenario.SchedulingRequest);
        Assert.InRange(urgent.TickCount, 1, 39);
        Assert.InRange(scheduling.TickCount, 1, 39);
    }


    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Dominatus.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
