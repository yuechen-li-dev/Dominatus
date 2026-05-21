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
        Assert.All(new[] { "codex-author", "chatgpt-reviewer", "claude-auditor", "release-prep" }, id => Assert.True(File.Exists(result.PacketPaths[id])));
        Assert.True(File.Exists(Path.Combine(outputDir, "packets", "LLM_REVIEW_PROMPT.md")));
    }


    [Fact]
    public void Dogfood_Run_CreatesPacketManifestArtifacts()
    {
        var result = ContextDogfoodDemo.Run(NewOutputDir());
        Assert.All(new[] { "codex-author", "chatgpt-reviewer", "claude-auditor", "release-prep" }, id => Assert.True(File.Exists(result.PacketManifestPaths[id])));
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
    public void Dogfood_ProjectDoesNotReferenceProviders()
    {
        var root = FindRepoRoot();
        var csproj = File.ReadAllText(Path.Combine(root, "samples", "Dominatus.Llm.ContextDogfood", "Dominatus.Llm.ContextDogfood.csproj"));
        Assert.DoesNotContain("Dominatus.Llm.OptFlow", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenAI", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SemanticKernel", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MCP", csproj, StringComparison.OrdinalIgnoreCase);
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
