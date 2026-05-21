using Dominatus.Llm.Context;
using Dominatus.Llm.ContextDogfood;

namespace Dominatus.Llm.ContextDogfood.Tests;

public class ContextDogfoodDemoTests
{
    [Fact]
    public void Dogfood_Run_CreatesExpectedArtifacts()
    {
        var outputDir = NewOutputDir();
        var result = ContextDogfoodDemo.Run(outputDir);

        Assert.True(File.Exists(result.JsonPath));
        Assert.True(File.Exists(result.BinaryContextPath));
        Assert.All(new[] { "codex-author", "chatgpt-reviewer", "claude-auditor", "release-prep", "pressure-test" }, id => Assert.True(File.Exists(result.PacketPaths[id])));
        Assert.True(File.Exists(Path.Combine(outputDir, "packets", "LLM_REVIEW_PROMPT.md")));
    }


    [Fact]
    public void Dogfood_Run_CreatesPacketManifestArtifacts()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir());
        Assert.All(new[] { "codex-author", "chatgpt-reviewer", "claude-auditor", "release-prep", "pressure-test" }, id => Assert.True(File.Exists(result.PacketManifestPaths[id])));
    }

    [Fact]
    public void Dogfood_PacketManifestContainsDiagnostics()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir());
        var manifest = LlmContextPacketManifestJson.Deserialize(File.ReadAllText(result.PacketManifestPaths["codex-author"]));
        Assert.NotEmpty(manifest.Diagnostics);
    }

    [Fact]
    public void Dogfood_PacketManifestIncludesStructuredLoadoutProvenance()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir());
        var manifest = LlmContextPacketManifestJson.Deserialize(File.ReadAllText(result.PacketManifestPaths["codex-author"]));
        Assert.Equal(LlmContextPacketSourceKind.Loadout, manifest.Provenance.SourceKind);
        Assert.Equal("codex-author", manifest.Provenance.LoadoutId);
        Assert.Equal("Codex Author", manifest.Provenance.LoadoutTitle);
    }

    [Fact]
    public void Dogfood_ManifestShowsIncludedAndOmittedChunks()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir());
        var manifest = LlmContextPacketManifestJson.Deserialize(File.ReadAllText(result.PacketManifestPaths["release-prep"]));
        Assert.NotEmpty(manifest.IncludedChunkIds);
        Assert.NotEmpty(manifest.OmittedChunkIds);
    }

    [Fact]
    public void Dogfood_BinaryContextManifestContainsStoreChunk()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir());
        var storeChunk = Assert.Single(result.Manifest.Chunks.Where(c => c.Id == LlmContextContainer.DefaultStoreChunkId));
        Assert.Equal(LlmContextContainer.StoreChunkFormat, storeChunk.Format);
    }

    [Fact]
    public void Dogfood_JsonAndBinaryLoadEquivalentStore()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir());
        var fromJson = LlmContextStoreJson.Load(result.JsonPath);
        var fromBinary = LlmContextContainer.Load(result.BinaryContextPath);

        Assert.Equal(fromJson.Id, fromBinary.Id);
        Assert.Equal(fromJson.Title, fromBinary.Title);
        Assert.Equal(fromJson.Chunks.Count, fromBinary.Chunks.Count);
        Assert.Equal(fromJson.Loadouts.Count, fromBinary.Loadouts.Count);
    }

    [Fact]
    public void Dogfood_LoadoutsProduceDifferentPackets()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir());
        var author = result.Packets["codex-author"].Text;
        var reviewer = result.Packets["chatgpt-reviewer"].Text;

        Assert.NotEqual(author, reviewer);
        Assert.Contains("doctrine", reviewer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("open-loop", author, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dogfood_PacketsContainExpectedDoctrine()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir());
        var reviewer = result.Packets["chatgpt-reviewer"].Text;
        Assert.Contains("Dominatus owns orchestration", reviewer, StringComparison.Ordinal);
        Assert.Contains("generated from explicit persisted chunks", reviewer, StringComparison.Ordinal);
    }

    [Fact]
    public void Dogfood_ReviewPromptMentionsManifestInspection()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir());
        var prompt = File.ReadAllText(Path.Combine(result.OutputDirectory, "packets", "LLM_REVIEW_PROMPT.md"));
        Assert.Contains(".manifest.json", prompt);
        Assert.Contains("Which omitted chunks would you have wanted?", prompt);
        Assert.Contains("packet provenance", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dogfood_LoadoutsIncludeGateCriticalChunks()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir());
        var releasePrep = LlmContextPacketManifestJson.Deserialize(File.ReadAllText(result.PacketManifestPaths["release-prep"]));
        Assert.Contains("dominatus.constraint.no-live-providers", releasePrep.IncludedChunkIds);
        Assert.Contains("dominatus.decision.refusal", releasePrep.IncludedChunkIds);
    }

    [Fact]
    public void Dogfood_AuthorAndAuditorLoadoutsIncludeDoctrineGuardrails()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir());
        var author = LlmContextPacketManifestJson.Deserialize(File.ReadAllText(result.PacketManifestPaths["codex-author"]));
        var auditor = LlmContextPacketManifestJson.Deserialize(File.ReadAllText(result.PacketManifestPaths["claude-auditor"]));

        Assert.Contains(author.IncludedChunkIds, id => id is "dominatus.doctrine.orchestration" or "dominatus.doctrine.context");
        Assert.Contains(auditor.IncludedChunkIds, id => id is "dominatus.doctrine.llm-role" or "dominatus.doctrine.orchestration");
    }

    [Fact]
    public void Dogfood_PressureTestProducesBudgetOmissions()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir());
        var pressure = LlmContextPacketManifestJson.Deserialize(File.ReadAllText(result.PacketManifestPaths["pressure-test"]));

        Assert.True(pressure.WasBudgetConstrained);
        Assert.Contains(pressure.Diagnostics, d => d.OmissionReason == LlmContextPacketOmissionReason.BudgetExceeded);
        Assert.Contains("dominatus.doctrine.orchestration", pressure.IncludedChunkIds);
    }

    [Fact]
    public void Dogfood_ManifestsExposeReadableEnumNames()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir());
        var json = File.ReadAllText(result.PacketManifestPaths["pressure-test"]);
        Assert.Contains("\"statusName\":", json, StringComparison.Ordinal);
        Assert.Contains("\"omissionReasonName\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Dogfood_ProjectDoesNotReferenceProviders()
    {
        var root = FindRepoRoot();
        var csproj = File.ReadAllText(Path.Combine(root, "samples", "Dominatus.Llm.ContextDogfood", "Dominatus.Llm.ContextDogfood.csproj"));
        Assert.DoesNotContain("Dominatus.Llm.OptFlow", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenAI", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SemanticKernel", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MCP", csproj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dogfood_RustPrimer_CreatesExpectedArtifacts()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir()).RustPrimer;
        Assert.True(File.Exists(result.JsonPath));
        Assert.True(File.Exists(result.BinaryContextPath));
        Assert.True(File.Exists(result.ManifestPath));
        Assert.All(new[] { "rust-author", "rust-reviewer", "rust-auditor" }, id =>
        {
            Assert.True(File.Exists(result.PacketPaths[id]));
            Assert.True(File.Exists(result.PacketManifestPaths[id]));
        });
        Assert.True(File.Exists(result.ReviewPromptPath));
    }

    [Fact]
    public void Dogfood_RustPrimer_JsonAndBinaryLoadEquivalentStore()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir()).RustPrimer;
        var fromJson = LlmContextStoreJson.Load(result.JsonPath);
        var fromBinary = LlmContextContainer.Load(result.BinaryContextPath);
        Assert.Equal(fromJson.Id, fromBinary.Id);
        Assert.Equal(fromJson.Title, fromBinary.Title);
        Assert.Equal(fromJson.Chunks.Count, fromBinary.Chunks.Count);
        Assert.Equal(fromJson.Loadouts.Count, fromBinary.Loadouts.Count);
    }

    [Fact]
    public void Dogfood_RustPrimer_LoadoutsProduceDifferentPackets()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir()).RustPrimer;
        var author = File.ReadAllText(result.PacketPaths["rust-author"]);
        var reviewer = File.ReadAllText(result.PacketPaths["rust-reviewer"]);
        var auditor = File.ReadAllText(result.PacketPaths["rust-auditor"]);
        Assert.NotEqual(author, reviewer);
        Assert.NotEqual(reviewer, auditor);
        Assert.Contains("restricted", reviewer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unsafe", auditor, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dogfood_RustPrimer_AuthorPacketContainsCoreRules()
    {
        var packet = File.ReadAllText(ContextDogfoodDemo.Run(NewOutputDir()).RustPrimer.PacketPaths["rust-author"]);
        Assert.Contains("owned data", packet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("short borrow", packet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cheap clone", packet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dogfood_RustPrimer_ReviewerPacketContainsReviewChecklist()
    {
        var packet = File.ReadAllText(ContextDogfoodDemo.Run(NewOutputDir()).RustPrimer.PacketPaths["rust-reviewer"]);
        Assert.Contains("review checklist", packet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reject", packet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dogfood_RustPrimer_AuditorPacketContainsRestrictedFootguns()
    {
        var packet = File.ReadAllText(ContextDogfoodDemo.Run(NewOutputDir()).RustPrimer.PacketPaths["rust-auditor"]);
        Assert.Contains("unsafe", packet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rc<RefCell", packet, StringComparison.Ordinal);
        Assert.Contains("Arc<Mutex", packet, StringComparison.Ordinal);
        Assert.Contains("async", packet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dogfood_RustPrimer_PacketsContainGoodAndBadExamples()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir()).RustPrimer;
        var merged = string.Join("\n", result.PacketPaths.Values.Select(File.ReadAllText));
        Assert.Contains("Good:", merged, StringComparison.Ordinal);
        Assert.Contains("Bad:", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void Dogfood_RustPrimer_ReviewPromptContainsPrimerQuestions()
    {
        var prompt = File.ReadAllText(ContextDogfoodDemo.Run(NewOutputDir()).RustPrimer.ReviewPromptPath);
        Assert.Contains("implement Rust", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("subset violations", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unsafe/interior-mutability/async footguns", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("examples", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PRIMER.context vs project-specific PROJECT.context", prompt, StringComparison.OrdinalIgnoreCase);
    }


    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Dominatus.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private static string NewOutputDir()
    {
        var p = Path.Combine(Path.GetTempPath(), "Dominatus.Llm.ContextDogfood.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }
}
