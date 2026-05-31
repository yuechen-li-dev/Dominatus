namespace Dominatus.RTSBenchmark.Tests;

public sealed class RtsBenchmarkDependencyTests
{
    [Fact]
    public void RtsBenchmark_NoLlmOrNetworkDependencies()
    {
        var repoRoot = FindRepoRoot();
        var sampleRoot = Path.Combine(repoRoot, "samples", "Dominatus.RTSBenchmark");
        var forbidden = new[]
        {
            "Dominatus.Llm.OptFlow",
            "OpenAI",
            "Anthropic",
            "Azure.AI.OpenAI",
            "OpenRouter",
            "SemanticKernel",
            "Microsoft.Graph"
        };

        var files = Directory.EnumerateFiles(sampleRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        var text = string.Join('\n', files.Select(File.ReadAllText));

        foreach (var token in forbidden)
            Assert.DoesNotContain(token, text, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Dominatus.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find Dominatus.slnx.");
    }
}
