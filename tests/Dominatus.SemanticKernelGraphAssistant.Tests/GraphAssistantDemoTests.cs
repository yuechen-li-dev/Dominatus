using System.Xml.Linq;
using Dominatus.Actuators.SemanticKernel;
using Dominatus.SemanticKernelGraphAssistant;

namespace Dominatus.SemanticKernelGraphAssistant.Tests;

public sealed class GraphAssistantDemoTests
{
    [Fact]
    public void GraphAssistant_NoApproval_UsesLlmCallToGenerateDraft()
    {
        var result = GraphAssistantDemo.Run(false);
        Assert.False(result.ApprovalGranted);
        Assert.True(result.UsedLlmCall || result.LlmCallCount > 0);
        Assert.Contains("deployment is still on track", result.DraftText ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.CreatedDraft);
        Assert.False(result.SentMail);
    }

    [Fact]
    public void GraphAssistant_NoApproval_DraftUsesGeneratedText()
    {
        var result = GraphAssistantDemo.Run(false);
        var draftResult = result.BlackboardOutputs["draftResult"];
        Assert.Contains(result.DraftText ?? string.Empty, draftResult, StringComparison.Ordinal);
        Assert.Contains("graph.mail.create_draft", result.InvokedFunctions);
        Assert.DoesNotContain("graph.mail.send_message", result.InvokedFunctions);
    }

    [Fact]
    public void GraphAssistant_WithApproval_UsesLlmCallAndSendsGeneratedText()
    {
        var result = GraphAssistantDemo.Run(true);
        Assert.True(result.ApprovalGranted);
        Assert.True(result.UsedLlmCall || result.LlmCallCount > 0);
        Assert.Contains("deployment is still on track", result.DraftText ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.SentMail);
        Assert.Contains(result.DraftText ?? string.Empty, result.BlackboardOutputs["sendResult"], StringComparison.Ordinal);
    }

    [Fact]
    public void GraphAssistant_UsesAiDecide_ForDraftAndSendModes()
    {
        var noApproval = GraphAssistantDemo.Run(false);
        var withApproval = GraphAssistantDemo.Run(true);
        Assert.Contains(noApproval.DecisionEvents, e => e.Contains("Ai.Decide chose DraftReply", StringComparison.Ordinal));
        Assert.Contains(withApproval.DecisionEvents, e => e.Contains("Ai.Decide chose SendApprovedReply", StringComparison.Ordinal));
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
    public void GraphAssistant_SendPolicyStillDeniesWithoutApprovalBeforeInvocation()
    {
        var result = GraphAssistantDemo.Run(false);
        Assert.DoesNotContain("graph.mail.send_message", result.InvokedFunctions);
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
