using Xunit;

namespace Dominatus.Core.Tests.Persistence;

public sealed class PersistenceNativeAotReadinessTests
{
    [Fact]
    public void BbJsonCodec_Source_DoesNotUseJsonSerializer()
    {
        var source = File.ReadAllText(FindFromRepoRoot("src/Dominatus.Core/Persistence/BbJsonCodec.cs"));
        Assert.DoesNotContain("JsonSerializer.Serialize(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Deserialize<", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PersistenceCode_Source_UsesContextBasedSerialization()
    {
        var saveSource = File.ReadAllText(FindFromRepoRoot("src/Dominatus.Core/Persistence/DominatusSave.cs"));
        var cursorSource = File.ReadAllText(FindFromRepoRoot("src/Dominatus.Core/Persistence/EventCursorCodec.cs"));

        Assert.DoesNotContain("JsonSerializer.Deserialize<", saveSource, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Deserialize<", cursorSource, StringComparison.Ordinal);
        Assert.Contains("DominatusJsonContext.Default", saveSource, StringComparison.Ordinal);
        Assert.Contains("DominatusJsonContext.Default", cursorSource, StringComparison.Ordinal);
    }

    private static string FindFromRepoRoot(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException($"Could not locate '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
