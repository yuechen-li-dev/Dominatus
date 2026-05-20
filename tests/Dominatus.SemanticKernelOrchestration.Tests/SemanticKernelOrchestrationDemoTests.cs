using Dominatus.SemanticKernelOrchestration;

namespace Dominatus.SemanticKernelOrchestration.Tests;

public class SemanticKernelOrchestrationDemoTests
{
    [Fact]
    public void Demo_CompletesAndProducesFinalReport()
    {
        var result = SemanticKernelOrchestrationDemo.Run();
        Assert.True(result.Completed);
        Assert.False(string.IsNullOrWhiteSpace(result.FinalReport));
        Assert.Contains("TinyNet", result.FinalReport);
        Assert.Contains("VisionMax", result.FinalReport);
        Assert.Contains("LlamaCalc", result.FinalReport);
        Assert.Contains("efficient", result.FinalReport, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.TickCount <= 200);
    }

    [Fact]
    public void Demo_UsesSemanticKernelFunctions()
    {
        var result = SemanticKernelOrchestrationDemo.Run();
        Assert.Equal(3, result.ResearchCalls);
        Assert.Equal(1, result.ComputeCalls);
        Assert.Equal(1, result.WriterCalls);
    }

    [Fact]
    public void Demo_PopulatesTaskLedgerInExpectedOrder()
    {
        var result = SemanticKernelOrchestrationDemo.Run();
        Assert.True(result.Completed);
        Assert.Contains("TinyNet", result.Ledger["FactsTinyNet"]);
        Assert.Contains("VisionMax", result.Ledger["FactsVisionMax"]);
        Assert.Contains("LlamaCalc", result.Ledger["FactsLlamaCalc"]);
        Assert.False(string.IsNullOrWhiteSpace(result.Ledger["DerivedComparison"]));
        Assert.False(string.IsNullOrWhiteSpace(result.Ledger["FinalReport"]));
        Assert.Equal("true", result.Ledger["TaskComplete"]);
    }

    [Fact]
    public void Demo_DoesNotRepeatResearchForCompletedModels()
    {
        var result = SemanticKernelOrchestrationDemo.Run();
        Assert.Equal("1", result.Ledger["ResearchTinyNetCount"]);
        Assert.Equal("1", result.Ledger["ResearchVisionMaxCount"]);
        Assert.Equal("1", result.Ledger["ResearchLlamaCalcCount"]);
    }

    [Fact]
    public void Demo_DoesNotReconsumeHistoricalWorkerInstruction()
    {
        var result = SemanticKernelOrchestrationDemo.Run();
        Assert.Equal(1, result.Events.Count(e => e == "assigned research TinyNet seq=1"));
        Assert.Equal(1, result.Events.Count(e => e.Contains("assigned research VisionMax", StringComparison.Ordinal)));
        Assert.Equal(1, result.Events.Count(e => e.Contains("assigned research LlamaCalc", StringComparison.Ordinal)));
    }

    [Fact]
    public void Demo_AwaitReportConsumesOnlyCurrentInstructionSequence()
    {
        var good = new WorkerReport(5, "research", "facts", "VisionMax", "ok");
        var wrongSeq = new WorkerReport(4, "research", "facts", "VisionMax", "old");
        var wrongKind = new WorkerReport(5, "research", "comparison", "VisionMax", "bad");
        var wrongSubject = new WorkerReport(5, "research", "facts", "TinyNet", "bad");

        Assert.True(SemanticKernelOrchestrationDemo.IsReportMatch(good, 5, "facts", "VisionMax"));
        Assert.False(SemanticKernelOrchestrationDemo.IsReportMatch(wrongSeq, 5, "facts", "VisionMax"));
        Assert.False(SemanticKernelOrchestrationDemo.IsReportMatch(wrongKind, 5, "facts", "VisionMax"));
        Assert.False(SemanticKernelOrchestrationDemo.IsReportMatch(wrongSubject, 5, "facts", "VisionMax"));
    }

    [Fact]
    public void Demo_DoesNotUseSemanticKernelOrchestrationApis()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var csproj = File.ReadAllText(Path.Combine(root, "samples", "Dominatus.SemanticKernelOrchestration", "Dominatus.SemanticKernelOrchestration.csproj"));
        Assert.DoesNotContain("Microsoft.SemanticKernel.Agents", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.SemanticKernel.Planners", csproj, StringComparison.OrdinalIgnoreCase);

        var code = File.ReadAllText(Path.Combine(root, "samples", "Dominatus.SemanticKernelOrchestration", "SemanticKernelOrchestrationDemo.cs"));
        Assert.DoesNotContain("Microsoft.SemanticKernel.Agents", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Planner", code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Demo_MetadataCatalogListsOnlyAllowedFunctions()
    {
        var result = SemanticKernelOrchestrationDemo.Run();
        var allowed = result.AllowedFunctions;
        Assert.Contains("research.lookup_model_facts", allowed);
        Assert.Contains("compute.compare_efficiency", allowed);
        Assert.Contains("writer.write_report", allowed);
        Assert.Equal(3, allowed.Count);
    }
}
