using Dominatus.Assets.Toml;
using Dominatus.Assets.Toml.AriadneDialogue;

namespace Dominatus.Assets.Toml.Tests;

public sealed class AssetPackReloadTests
{
    [Fact]
    public void AssetPackReloader_ReloadDirectory_DetectsAddedAsset()
    {
        using var oldDirectory = CreateTempDirectory();
        using var newDirectory = CreateTempDirectory();
        WriteSimpleToml(oldDirectory.Path, "one.toml", "asset.one", "One");
        WriteSimpleToml(newDirectory.Path, "one.toml", "asset.one", "One");
        WriteSimpleToml(newDirectory.Path, "two.toml", "asset.two", "Two");
        var oldPack = LoadSimplePack(oldDirectory.Path);

        var result = TomlAssetPackReloader.ReloadDirectory(oldPack, newDirectory.Path, GetSimpleId);

        Assert.True(result.Success, AssetDiagnosticFormatter.FormatMany(result.Diagnostics));
        Assert.Equal([new AssetId("asset.two")], result.Added);
        Assert.Same(result.NewPack, result.EffectivePack);
        Assert.False(result.UsedOldPack);
    }

    [Fact]
    public void AssetPackReloader_ReloadDirectory_DetectsRemovedAsset()
    {
        using var oldDirectory = CreateTempDirectory();
        using var newDirectory = CreateTempDirectory();
        WriteSimpleToml(oldDirectory.Path, "one.toml", "asset.one", "One");
        WriteSimpleToml(oldDirectory.Path, "two.toml", "asset.two", "Two");
        WriteSimpleToml(newDirectory.Path, "one.toml", "asset.one", "One");
        var oldPack = LoadSimplePack(oldDirectory.Path);

        var result = TomlAssetPackReloader.ReloadDirectory(oldPack, newDirectory.Path, GetSimpleId);

        Assert.True(result.Success, AssetDiagnosticFormatter.FormatMany(result.Diagnostics));
        Assert.Equal([new AssetId("asset.two")], result.Removed);
    }

    [Fact]
    public void AssetPackReloader_ReloadDirectory_DetectsChangedAssetByContentHash()
    {
        using var oldDirectory = CreateTempDirectory();
        using var newDirectory = CreateTempDirectory();
        WriteSimpleToml(oldDirectory.Path, "one.toml", "asset.one", "One");
        WriteSimpleToml(newDirectory.Path, "one.toml", "asset.one", "One Reloaded");
        var oldPack = LoadSimplePack(oldDirectory.Path);

        var result = TomlAssetPackReloader.ReloadDirectory(oldPack, newDirectory.Path, GetSimpleId);

        Assert.True(result.Success, AssetDiagnosticFormatter.FormatMany(result.Diagnostics));
        Assert.Equal([new AssetId("asset.one")], result.Changed);
        Assert.NotEqual(
            oldPack.Assets[new AssetId("asset.one")].ContentHash,
            result.NewPack!.Assets[new AssetId("asset.one")].ContentHash);
    }

    [Fact]
    public void AssetPackReloader_ReloadDirectory_DetectsUnchangedAsset()
    {
        using var oldDirectory = CreateTempDirectory();
        using var newDirectory = CreateTempDirectory();
        WriteSimpleToml(oldDirectory.Path, "one.toml", "asset.one", "One");
        WriteSimpleToml(newDirectory.Path, "one.toml", "asset.one", "One");
        var oldPack = LoadSimplePack(oldDirectory.Path);

        var result = TomlAssetPackReloader.ReloadDirectory(oldPack, newDirectory.Path, GetSimpleId);

        Assert.True(result.Success, AssetDiagnosticFormatter.FormatMany(result.Diagnostics));
        Assert.Equal([new AssetId("asset.one")], result.Unchanged);
        Assert.Empty(result.Changed);
    }

    [Fact]
    public void AssetPackReloader_ReloadFailure_KeepsOldPackWhenConfigured()
    {
        using var oldDirectory = CreateTempDirectory();
        using var newDirectory = CreateTempDirectory();
        WriteSimpleToml(oldDirectory.Path, "one.toml", "asset.one", "One");
        File.WriteAllText(Path.Combine(newDirectory.Path, "broken.toml"), "id = \"unterminated");
        var oldPack = LoadSimplePack(oldDirectory.Path);

        var result = TomlAssetPackReloader.ReloadDirectory(oldPack, newDirectory.Path, GetSimpleId);

        Assert.False(result.Success);
        Assert.True(result.UsedOldPack);
        Assert.Same(oldPack, result.EffectivePack);
        Assert.Contains(result.Diagnostics, d => d.Code == "toml.parse" && d.Severity == AssetDiagnosticSeverity.Error);
    }

    [Fact]
    public void AssetPackReloader_ReloadFailure_DoesNotMutateOldPack()
    {
        using var oldDirectory = CreateTempDirectory();
        using var newDirectory = CreateTempDirectory();
        WriteSimpleToml(oldDirectory.Path, "one.toml", "asset.one", "One");
        WriteSimpleToml(newDirectory.Path, "one.toml", "asset.one", "One Reloaded");
        File.WriteAllText(Path.Combine(newDirectory.Path, "broken.toml"), "id = \"unterminated");
        var oldPack = LoadSimplePack(oldDirectory.Path);
        var oldTitle = oldPack.Assets[new AssetId("asset.one")].Asset.Title;
        var oldHash = oldPack.Assets[new AssetId("asset.one")].ContentHash;

        var result = TomlAssetPackReloader.ReloadDirectory(oldPack, newDirectory.Path, GetSimpleId);

        Assert.False(result.Success);
        Assert.Same(oldPack, result.EffectivePack);
        Assert.Equal(oldTitle, oldPack.Assets[new AssetId("asset.one")].Asset.Title);
        Assert.Equal(oldHash, oldPack.Assets[new AssetId("asset.one")].ContentHash);
    }

    [Fact]
    public void AssetPackReloadReportFormatter_FormatsSuccess()
    {
        using var oldDirectory = CreateTempDirectory();
        using var newDirectory = CreateTempDirectory();
        WriteSimpleToml(oldDirectory.Path, "one.toml", "asset.one", "One");
        WriteSimpleToml(newDirectory.Path, "one.toml", "asset.one", "One Reloaded");
        var oldPack = LoadSimplePack(oldDirectory.Path);
        var result = TomlAssetPackReloader.ReloadDirectory(oldPack, newDirectory.Path, GetSimpleId);

        var formatted = AssetPackReloadReportFormatter.Format(result);

        Assert.Contains("Asset reload: OK", formatted, StringComparison.Ordinal);
        Assert.Contains("Changed: 1", formatted, StringComparison.Ordinal);
        Assert.Contains("* asset.one", formatted, StringComparison.Ordinal);
        Assert.Contains("Effective pack: new", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetPackReloadReportFormatter_FormatsFailureKeepingOldPack()
    {
        using var oldDirectory = CreateTempDirectory();
        using var newDirectory = CreateTempDirectory();
        WriteSimpleToml(oldDirectory.Path, "one.toml", "asset.one", "One");
        File.WriteAllText(Path.Combine(newDirectory.Path, "broken.toml"), "id = \"unterminated");
        var oldPack = LoadSimplePack(oldDirectory.Path);
        var result = TomlAssetPackReloader.ReloadDirectory(oldPack, newDirectory.Path, GetSimpleId);

        var formatted = AssetPackReloadReportFormatter.Format(result);

        Assert.Contains("Asset reload: FAILED — keeping previous pack", formatted, StringComparison.Ordinal);
        Assert.Contains("Errors: 1", formatted, StringComparison.Ordinal);
        Assert.Contains("Effective pack: old", formatted, StringComparison.Ordinal);
        Assert.Contains("Diagnostics:", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void AriadneSample_ReloadDemo_DetectsChangedDialogue()
    {
        var dialogueDirectory = Path.Combine(AppContext.BaseDirectory, "dialogue");
        var localizationPath = Path.Combine(AppContext.BaseDirectory, "localization", "en.csv");
        var localization = SampleLocalizationCsvLoader.LoadFile(localizationPath).Value!;

        var result = DialogueReloadDemo.Run(dialogueDirectory, localization);

        Assert.True(result.Success, AssetDiagnosticFormatter.FormatMany(result.ReloadResult.Diagnostics.Concat(result.TraversalResult.Diagnostics)));
        Assert.Equal([new AssetId("dialogue.north_road_job")], result.ReloadResult.Changed);
        Assert.Contains("offer_quest north_road_bandits", result.TraversalResult.EffectsRun);
        Assert.Contains("Reload demo:", DialogueReloadDemo.Format(result), StringComparison.Ordinal);
    }

    [Fact]
    public void ContentHash_IsStableForSameFileContent()
    {
        using var firstDirectory = CreateTempDirectory();
        using var secondDirectory = CreateTempDirectory();
        WriteSimpleToml(firstDirectory.Path, "one.toml", "asset.one", "One");
        WriteSimpleToml(secondDirectory.Path, "renamed.toml", "asset.one", "One");

        var first = LoadSimplePack(firstDirectory.Path);
        var second = LoadSimplePack(secondDirectory.Path);

        Assert.NotNull(first.Assets[new AssetId("asset.one")].ContentHash);
        Assert.Equal(first.Assets[new AssetId("asset.one")].ContentHash, second.Assets[new AssetId("asset.one")].ContentHash);
    }

    private static AssetPack<SimpleAsset> LoadSimplePack(string directory)
    {
        var result = TomlAssetPackLoader.LoadDirectory<SimpleAsset>(directory, GetSimpleId);
        Assert.True(result.Success, AssetDiagnosticFormatter.FormatMany(result.Diagnostics));
        return result.Pack!;
    }

    private static AssetId GetSimpleId(SimpleAsset asset) => new(asset.Id);

    private static string WriteSimpleToml(string directory, string fileName, string id, string title)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, $$"""
            id = "{{id}}"
            title = "{{title}}"
            score = 1
            """);
        return path;
    }

    private static TempDirectory CreateTempDirectory() => new(Path.Combine(Path.GetTempPath(), $"dominatus-assets-reload-{Guid.NewGuid():N}"));

    public sealed record SimpleAsset
    {
        public required string Id { get; init; }

        public required string Title { get; init; }

        public int Score { get; init; }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
