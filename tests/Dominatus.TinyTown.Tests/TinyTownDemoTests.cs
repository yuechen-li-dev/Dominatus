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
        Assert.Contains(result.DialogueLines, line => line.Contains("Maya: So...", StringComparison.Ordinal) && line.Contains("Theo:", StringComparison.Ordinal));
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
    public void TinyTown_Relationships_AreIncludedInResult()
    {
        var result = TinyTownDemo.RunScenario(0);

        Assert.Contains(result.Relationships, r => r.A == "maya" && r.B == "theo" && r.UnresolvedIssueId == "missed-celebration");
        Assert.Contains(result.Relationships, r => r.A == "lina" && r.B == "maya");
        Assert.Contains(result.Relationships, r => r.A == "nia" && r.B == "theo");
    }

    [Fact]
    public void TinyTown_Chat_ParsesStructuredDialogueOutcome()
    {
        var result = TinyTownDemo.RunAwkwardMayaTheoConversation();
        var outcome = Assert.Single(result.DialogueOutcomes);

        Assert.Equal("awkward", outcome.Tone);
        Assert.Equal("partial_repair", outcome.Outcome);
        Assert.Contains("Maya: So...", outcome.Dialogue, StringComparison.Ordinal);
        Assert.Contains("Theo:", outcome.Dialogue, StringComparison.Ordinal);
    }

    [Fact]
    public void TinyTown_Chat_AppliesRelationshipDeltas()
    {
        var before = Relationship(TinyTownDemo.RunScenario(0), "maya", "theo");
        var after = Relationship(TinyTownDemo.RunAwkwardMayaTheoConversation(), "maya", "theo");

        Assert.Equal(before.Affinity + 0.08f, after.Affinity, precision: 3);
        Assert.Equal(before.Tension - 0.12f, after.Tension, precision: 3);
        Assert.Equal(0, after.LastInteractionTick);
    }

    [Fact]
    public void TinyTown_Chat_AppendsMemoryRecord()
    {
        var result = TinyTownDemo.RunAwkwardMayaTheoConversation();
        var memory = Assert.Single(result.Memories, m => m.Kind == "dialogue");

        Assert.Equal("memory.maya-theo.chat.0", memory.Id);
        Assert.Contains("awkward but honest", memory.Summary, StringComparison.Ordinal);
        Assert.Contains("maya", memory.TownieIds);
        Assert.Contains("theo", memory.TownieIds);
    }

    [Fact]
    public void TinyTown_Chat_ContextIncludesPriorMemory()
    {
        var result = TinyTownDemo.RunAwkwardMayaTheoConversation();
        var context = Assert.Single(result.LlmCallContexts);

        Assert.Contains("missed Maya", context, StringComparison.Ordinal);
        Assert.Contains("work celebration", context, StringComparison.Ordinal);
    }

    [Fact]
    public void TinyTown_InvalidRelationshipDeltas_AreClamped()
    {
        var result = TinyTownDemo.RunInvalidDeltaClampScenario();
        var outcome = Assert.Single(result.DialogueOutcomes);
        var relationship = Relationship(result, "maya", "theo");

        Assert.Equal(0.25f, outcome.AffinityDelta);
        Assert.Equal(-0.25f, outcome.TensionDelta);
        Assert.InRange(relationship.Affinity, 0f, 1f);
        Assert.InRange(relationship.Tension, 0f, 1f);
        Assert.Equal(0.90f, relationship.Affinity, precision: 3);
        Assert.Equal(0.20f, relationship.Tension, precision: 3);
    }

    [Fact]
    public void TinyTown_NonChatActions_DoNotModifyRelationshipsViaLlm()
    {
        var before = TinyTownDemo.RunScenario(0);
        var result = TinyTownDemo.RunHungryScenario();

        Assert.Empty(result.DialogueOutcomes);
        Assert.Equal(0, result.LlmCallCount);
        Assert.Equal(before.Relationships, result.Relationships);
    }

    [Fact]
    public void TinyTown_DeterministicAcrossRuns_WithRelationshipMemory()
    {
        var first = TinyTownDemo.RunAwkwardMayaTheoConversation();
        var second = TinyTownDemo.RunAwkwardMayaTheoConversation();

        Assert.Equal(first.Relationships, second.Relationships);
        Assert.Equal(first.Memories.Select(MemoryKey), second.Memories.Select(MemoryKey));
        Assert.Equal(first.DialogueOutcomes, second.DialogueOutcomes);
    }

    [Fact]
    public void TinyTown_LlmIsDmNotAgent_DocsOrResultInvariant()
    {
        var result = TinyTownDemo.RunAwkwardMayaTheoConversation();

        Assert.Contains(result.EventLog, e => e.Contains("DM scene outcome", StringComparison.Ordinal));
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

    private static string MemoryKey(TownMemoryRecord memory)
        => $"{memory.Id}|{memory.Tick}|{string.Join(",", memory.TownieIds)}|{memory.Kind}|{memory.Summary}";

    private static RelationshipSnapshot Relationship(TinyTownSimulationResult result, string a, string b)
    {
        var first = string.CompareOrdinal(a, b) <= 0 ? a : b;
        var second = string.CompareOrdinal(a, b) <= 0 ? b : a;
        return result.Relationships.Single(r => r.A == first && r.B == second);
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
