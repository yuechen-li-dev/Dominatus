using System.Xml.Linq;
using Dominatus.Actuators.SemanticKernel;
using Dominatus.SemanticKernelGraphAssistant;

namespace Dominatus.SemanticKernelGraphAssistant.Tests;

public sealed class GraphAssistantDemoTests
{
    [Fact]
    public void GraphAssistant_NoApproval_CreatesDraftButDoesNotSend()
    {
        var result = GraphAssistantDemo.Run(false);
        Assert.False(result.ApprovalGranted);
        Assert.True(result.CreatedDraft);
        Assert.False(result.SentMail);
        Assert.False(result.CreatedCalendarEvent);
        Assert.Contains("graph.mail.create_draft", result.InvokedFunctions);
        Assert.DoesNotContain("graph.mail.send_message", result.InvokedFunctions);
        Assert.DoesNotContain("graph.calendar.create_event", result.InvokedFunctions);
    }

    [Fact]
    public void GraphAssistant_WithApproval_SendsOrCreatesApprovedAction()
    {
        var result = GraphAssistantDemo.Run(true);
        Assert.True(result.ApprovalGranted);
        Assert.True(result.SentMail);
        Assert.Contains("graph.mail.send_message", result.InvokedFunctions);
    }

    [Fact]
    public void GraphAssistant_UsesAiDecide()
    {
        var result = GraphAssistantDemo.Run(false);
        Assert.Contains(result.DecisionEvents, e => e.Contains("Ai.Decide", StringComparison.Ordinal));
        Assert.Contains(result.DecisionEvents, e => e.Contains("chose", StringComparison.Ordinal));
    }

    [Fact]
    public void GraphAssistant_UsesGraphProfileAllowlist()
    {
        var result = GraphAssistantDemo.Run(true);
        var profile = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendar();
        var allowed = profile.ToAllowedFunctions().Select(x => $"{x.PluginName}.{x.FunctionName}").ToHashSet(StringComparer.Ordinal);
        Assert.All(result.InvokedFunctions, f => Assert.Contains(f, allowed));
    }

    [Fact]
    public void GraphAssistant_NoApproval_PolicyDeniesSendBeforeInvocation()
    {
        var result = GraphAssistantDemo.Run(false);
        Assert.DoesNotContain("graph.mail.send_message", result.InvokedFunctions);
    }

    [Fact]
    public void GraphAssistant_NoLiveGraphDependencies()
    {
        var root = FindRepoRoot();
        var sampleProject = Path.Combine(root, "samples", "Dominatus.SemanticKernelGraphAssistant", "Dominatus.SemanticKernelGraphAssistant.csproj");
        var doc = XDocument.Load(sampleProject);
        var text = doc.ToString();
        Assert.DoesNotContain("Microsoft.Graph", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Azure.Identity", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MSAL", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mcp", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GraphAssistant_CompletesWithinMaxTicks()
    {
        var result = GraphAssistantDemo.Run(false);
        Assert.InRange(result.TickCount, 1, 39);
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
