using Dominatus.TinyTown;

namespace Dominatus.TinyTown.Tests;

public sealed class TinyTownDemoTests
{
    [Fact]
    public void TinyTown_Run_CompletesAndProducesTownieSnapshots()
    {
        var result = TinyTownDemo.Run(12);

        Assert.Equal(12, result.TicksRun);
        Assert.Equal(4, result.FinalTownies.Count);
        Assert.NotEmpty(result.EventLog);
        Assert.True(result.UsedAiDecide);
    }

    [Fact]
    public void TinyTown_HungryTownie_ChoosesEat()
    {
        var result = TinyTownDemo.RunHungryScenario();
        var maya = result.FinalTownies.Single(x => x.Id == "maya");

        Assert.Equal("Eat", maya.CurrentAction);
        Assert.Contains(result.EventLog, e => e.Contains("Maya ate", StringComparison.Ordinal));
        Assert.True(maya.Hunger > 0.05f);
    }

    [Fact]
    public void TinyTown_TiredTownie_ChoosesSleep()
    {
        var result = TinyTownDemo.RunTiredScenario();
        var maya = result.FinalTownies.Single(x => x.Id == "maya");

        Assert.Equal("Sleep", maya.CurrentAction);
        Assert.Contains(result.EventLog, e => e.Contains("Maya slept", StringComparison.Ordinal));
        Assert.True(maya.Energy > 0.05f);
    }

    [Fact]
    public void TinyTown_WorkSchedule_CausesGoToWork()
    {
        var result = TinyTownDemo.RunWorkScenario();
        var maya = result.FinalTownies.Single(x => x.Id == "maya");

        Assert.Equal("GoToWork", maya.CurrentAction);
        Assert.Equal("Work", maya.Location);
        Assert.Contains(result.EventLog, e => e.Contains("Maya went to Work", StringComparison.Ordinal));
    }

    [Fact]
    public void TinyTown_LowSocial_TriggersVisitOrChat()
    {
        var result = TinyTownDemo.RunSocialScenario();

        Assert.Contains(result.EventLog, e => e.Contains("requested visit", StringComparison.Ordinal) || e.Contains(" Chat with ", StringComparison.Ordinal));
    }

    [Fact]
    public void TinyTown_ChatUsesLlmCall()
    {
        var result = TinyTownDemo.RunSocialScenario();

        Assert.True(result.LlmCallCount > 0);
        Assert.Contains("Maya: Good to see you, Theo. Work has been a lot today.", result.DialogueLines);
        Assert.Contains(result.EventLog, e => e.Contains("Chat", StringComparison.Ordinal));
    }

    [Fact]
    public void TinyTown_NonDialogueActionsDoNotCallLlm()
    {
        Assert.Equal(0, TinyTownDemo.RunHungryScenario().LlmCallCount);
        Assert.Equal(0, TinyTownDemo.RunTiredScenario().LlmCallCount);
        Assert.Equal(0, TinyTownDemo.RunWorkScenario().LlmCallCount);
    }

    [Fact]
    public void TinyTown_SocialCoordinationUsesMailboxOrEvents()
    {
        var result = TinyTownDemo.RunScenario(3, new TinyTownScenarioOptions
        {
            Hunger = HighAll(), Energy = HighAll(), Fun = HighAll(), Hygiene = HighAll(), Bladder = HighAll(),
            Social = new Dictionary<string, float> { ["maya"] = 0.1f, ["theo"] = 0.4f },
            Locations = new Dictionary<string, string> { ["maya"] = "MayaHome", ["theo"] = "TheoHome" }
        });

        var requested = result.EventLog.Single(e => e.Contains("Maya requested visit with Theo", StringComparison.Ordinal));
        var received = result.EventLog.Single(e => e.Contains("Theo received visit request from Maya", StringComparison.Ordinal));
        Assert.StartsWith("tick 0:", requested);
        Assert.StartsWith("tick 1:", received);
    }

    [Fact]
    public void TinyTown_DeterministicAcrossRuns()
    {
        var first = TinyTownDemo.Run(25);
        var second = TinyTownDemo.Run(25);

        Assert.Equal(first.FinalTownies, second.FinalTownies);
        Assert.Equal(first.EventLog, second.EventLog);
        Assert.Equal(first.DialogueLines, second.DialogueLines);
    }

    [Fact]
    public void TinyTown_NoLiveDependencies()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var csproj = File.ReadAllText(Path.Combine(repoRoot, "samples", "Dominatus.TinyTown", "Dominatus.TinyTown.csproj"));
        var forbidden = new[] { "OpenAI", "Anthropic", "Azure.AI.OpenAI", "Microsoft.Graph", "Azure.Identity", "SemanticKernel", "MCP", "OpenRouter" };

        foreach (var package in forbidden)
        {
            Assert.DoesNotContain(package, csproj, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("Dominatus.Llm.OptFlow", csproj, StringComparison.Ordinal);
    }

    private static string FindRepoRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dominatus.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root containing Dominatus.slnx.");
    }

    private static Dictionary<string, float> HighAll() => new(StringComparer.Ordinal)
    {
        ["maya"] = 0.95f, ["theo"] = 0.95f, ["lina"] = 0.95f, ["nia"] = 0.95f
    };
}
