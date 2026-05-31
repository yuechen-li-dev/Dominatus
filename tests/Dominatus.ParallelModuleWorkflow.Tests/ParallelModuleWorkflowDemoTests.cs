using Dominatus.ParallelModuleWorkflow;

namespace Dominatus.ParallelModuleWorkflow.Tests;

public sealed class ParallelModuleWorkflowDemoTests
{
    [Fact]
    public async Task ParallelWorkflow_Run_CompletesAndProducesFinalReport()
    {
        var result = await ParallelModuleWorkflowDemo.RunAsync();

        Assert.Contains("AuthContract v1", result.Contract, StringComparison.Ordinal);
        Assert.Contains("Auth", result.FinalReport, StringComparison.Ordinal);
        Assert.Contains("API", result.FinalReport, StringComparison.Ordinal);
        Assert.Contains("Database", result.FinalReport, StringComparison.Ordinal);
        Assert.Contains("Frontend", result.FinalReport, StringComparison.Ordinal);
        Assert.Equal(4, result.ModuleResults.Count);
    }

    [Fact]
    public async Task ParallelWorkflow_UsesLlmCallForAllModules()
    {
        var result = await ParallelModuleWorkflowDemo.RunAsync();
        var stableIds = result.ModuleResults.Select(module => module.StableId).ToArray();

        Assert.True(result.UsedLlmCall);
        Assert.Contains(ParallelModuleWorkflowDemo.AuthStableId, stableIds);
        Assert.Contains(ParallelModuleWorkflowDemo.ApiStableId, stableIds);
        Assert.Contains(ParallelModuleWorkflowDemo.DatabaseStableId, stableIds);
        Assert.Contains(ParallelModuleWorkflowDemo.FrontendStableId, stableIds);
    }

    [Fact]
    public async Task ParallelWorkflow_RunsDependentModulesAfterContract()
    {
        var result = await ParallelModuleWorkflowDemo.RunAsync();
        var auth = Assert.Single(result.ModuleResults, module => module.Module == ModuleName.Auth);
        var dependentModules = result.ModuleResults.Where(module => module.Module != ModuleName.Auth).ToArray();

        Assert.All(dependentModules, module => Assert.True(auth.CompletedUtc <= module.StartedUtc));
        Assert.True(
            IndexOf(result.Events, "Auth contract ready") < IndexOf(result.Events, "Starting parallel module workers"),
            "The coordinator should publish the contract-ready event before launching dependent workers.");
    }

    [Fact]
    public async Task ParallelWorkflow_RunsApiDatabaseFrontendInParallel()
    {
        var result = await ParallelModuleWorkflowDemo.RunAsync();

        Assert.True(result.MaxObservedConcurrency >= 3);
        Assert.True(IndexOf(result.Events, "Api started") < IndexOf(result.Events, "Api completed"));
        Assert.True(IndexOf(result.Events, "Database started") < IndexOf(result.Events, "Database completed"));
        Assert.True(IndexOf(result.Events, "Frontend started") < IndexOf(result.Events, "Frontend completed"));
        Assert.True(IndexOf(result.Events, "Api started") < IndexOf(result.Events, "Parallel module workers completed"));
        Assert.True(IndexOf(result.Events, "Database started") < IndexOf(result.Events, "Parallel module workers completed"));
        Assert.True(IndexOf(result.Events, "Frontend started") < IndexOf(result.Events, "Parallel module workers completed"));
        Assert.True(IndexOf(result.Events, "Api completed") > IndexOf(result.Events, "Frontend started"));
        Assert.True(IndexOf(result.Events, "Database completed") > IndexOf(result.Events, "Frontend started"));
        Assert.True(IndexOf(result.Events, "Frontend completed") > IndexOf(result.Events, "Frontend started"));
    }

    [Fact]
    public async Task ParallelWorkflow_MergesResultsDeterministically()
    {
        var result = await ParallelModuleWorkflowDemo.RunAsync();

        Assert.Equal(
        [
            ModuleName.Auth,
            ModuleName.Api,
            ModuleName.Database,
            ModuleName.Frontend
        ],
        result.ModuleResults.Select(module => module.Module));
    }

    [Fact]
    public async Task ParallelWorkflow_NoLiveDependencies()
    {
        string csproj = await File.ReadAllTextAsync(SampleProjectPath());

        string[] forbidden =
        [
            "OpenAI",
            "Anthropic",
            "Azure.AI.OpenAI",
            "Microsoft.Graph",
            "Azure.Identity",
            "MSAL",
            "SemanticKernel.Planners",
            "SemanticKernel.Agents",
            "SemanticKernel.Orchestration",
            "MCP"
        ];

        foreach (string forbiddenDependency in forbidden)
        {
            Assert.DoesNotContain(forbiddenDependency, csproj, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("Dominatus.Llm.OptFlow", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParallelWorkflow_CompletesWithoutNetwork()
    {
        var result = await ParallelModuleWorkflowDemo.RunAsync();

        Assert.True(result.UsedLlmCall);
        Assert.Equal(4, result.ModuleResults.Select(module => module.StableId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(result.ModuleResults, module => Assert.StartsWith("parallel.", module.StableId, StringComparison.Ordinal));
    }

    private static int IndexOf(IReadOnlyList<string> events, string expected)
    {
        int index = events.ToList().FindIndex(e => string.Equals(e, expected, StringComparison.Ordinal));
        Assert.True(index >= 0, $"Expected event '{expected}' was not found.");
        return index;
    }

    private static string SampleProjectPath()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string? directory = baseDirectory;
        while (directory is not null)
        {
            string candidate = Path.Combine(directory, "samples", "Dominatus.ParallelModuleWorkflow", "Dominatus.ParallelModuleWorkflow.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException("Could not locate the parallel module workflow sample project from test output directory.", baseDirectory);
    }
}
