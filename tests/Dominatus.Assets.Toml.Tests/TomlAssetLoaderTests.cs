using Dominatus.Assets.Toml;
using Dominatus.Assets.Toml.AriadneDialogue;

namespace Dominatus.Assets.Toml.Tests;

public sealed class TomlAssetLoaderTests
{
    [Fact]
    public void TomlAssetLoader_LoadString_MapsValidTomlToRecord()
    {
        var result = TomlAssetLoader.LoadString<SimpleAsset>("""
            id = "asset.one"
            title = "Asset One"
            score = 7
            """);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Value);
        Assert.Equal("asset.one", result.Value.Id);
        Assert.Equal("Asset One", result.Value.Title);
        Assert.Equal(7, result.Value.Score);
    }

    [Fact]
    public void TomlAssetLoader_LoadString_ReturnsParseDiagnosticForInvalidToml()
    {
        var result = TomlAssetLoader.LoadString<SimpleAsset>("id = \"unterminated");

        Assert.False(result.Success);
        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics, d => d.Severity == AssetDiagnosticSeverity.Error && d.Code == "TOML_PARSE");
    }

    [Fact]
    public void TomlAssetLoader_LoadFile_UsesSourcePathInDiagnostics()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dominatus-invalid-{Guid.NewGuid():N}.toml");
        File.WriteAllText(path, "id = \"unterminated");

        try
        {
            var result = TomlAssetLoader.LoadFile<SimpleAsset>(path);

            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, d => d.SourcePath == path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void AssetId_RejectsEmptyOrWhitespace(string value)
    {
        Assert.Throws<ArgumentException>(() => new AssetId(value));
    }

    [Fact]
    public void AssetId_IsCaseSensitiveDictionaryKey()
    {
        var lower = new AssetId("dialogue.blacksmith_intro");
        var upper = new AssetId("dialogue.Blacksmith_Intro");
        var dictionary = new Dictionary<AssetId, string> { [lower] = "lower", [upper] = "upper" };

        Assert.Equal(2, dictionary.Count);
        Assert.Equal("lower", dictionary[lower]);
        Assert.Equal("upper", dictionary[upper]);
    }

    [Fact]
    public void AssetValidator_CanRejectMissingReference()
    {
        var result = TomlAssetLoader.LoadString<ReferenceAsset>("target = \"missing.node\"", new ReferenceAssetValidator());

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "TEST_REFERENCE_MISSING");
    }

    [Fact]
    public void AriadneDialogueSample_LoadsBlacksmithToml()
    {
        var result = LoadBlacksmith();

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Value);
        Assert.Equal("dialogue.blacksmith_intro", result.Value.Id);
        Assert.Equal("greeting", result.Value.Start);
        Assert.Equal(3, result.Value.Nodes.Count);
    }

    [Fact]
    public void AriadneDialogueSample_ValidatorAcceptsValidGraph()
    {
        var result = LoadBlacksmith();

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == AssetDiagnosticSeverity.Error);
    }

    [Fact]
    public void AriadneDialogueSample_ValidatorRejectsMissingStartNode()
    {
        var dialogue = new DialogueAsset
        {
            Id = "dialogue.test",
            Title = "Test",
            Start = "missing",
            Nodes = new Dictionary<string, DialogueNodeAsset>
            {
                ["only"] = new() { Speaker = "Narrator", Text = "Hello" }
            }
        };

        var diagnostics = new DialogueAssetValidator().Validate(dialogue, new AssetValidationContext { SourcePath = "inline" });

        Assert.Contains(diagnostics, d => d.Code == "DIALOGUE_START_MISSING");
    }

    [Fact]
    public void AriadneDialogueSample_ValidatorRejectsChoiceToMissingNode()
    {
        var dialogue = new DialogueAsset
        {
            Id = "dialogue.test",
            Title = "Test",
            Start = "start",
            Nodes = new Dictionary<string, DialogueNodeAsset>
            {
                ["start"] = new()
                {
                    Speaker = "Narrator",
                    Text = "Hello",
                    Choices = [new DialogueChoiceAsset { Id = "go", Text = "Go", Next = "missing" }]
                }
            }
        };

        var diagnostics = new DialogueAssetValidator().Validate(dialogue, new AssetValidationContext { SourcePath = "inline" });

        Assert.Contains(diagnostics, d => d.Code == "DIALOGUE_CHOICE_TARGET_MISSING");
    }

    [Fact]
    public void TomlAssetPackLoader_LoadDirectory_LoadsMultipleTomlFiles()
    {
        using var temp = CreateTempDirectory();
        WriteSimpleToml(temp.Path, "one.toml", "asset.one", "One");
        WriteSimpleToml(temp.Path, "two.toml", "asset.two", "Two");

        var result = TomlAssetPackLoader.LoadDirectory<SimpleAsset>(temp.Path, asset => new AssetId(asset.Id));

        Assert.True(result.Success, FormatDiagnostics(result.Diagnostics));
        Assert.NotNull(result.Pack);
        Assert.Equal(2, result.Pack.Assets.Count);
        Assert.True(result.Pack.TryGet(new AssetId("asset.one"), out var one));
        Assert.Equal("One", one.Title);
    }

    [Fact]
    public void TomlAssetPackLoader_LoadFiles_LoadsExplicitFileList()
    {
        using var temp = CreateTempDirectory();
        var one = WriteSimpleToml(temp.Path, "one.toml", "asset.one", "One");
        var two = WriteSimpleToml(temp.Path, "two.data", "asset.two", "Two");

        var result = TomlAssetPackLoader.LoadFiles<SimpleAsset>([one, two], asset => new AssetId(asset.Id));

        Assert.True(result.Success, FormatDiagnostics(result.Diagnostics));
        Assert.NotNull(result.Pack);
        Assert.Equal(2, result.Pack.Assets.Count);
    }

    [Fact]
    public void TomlAssetPackLoader_DuplicateAssetIds_ReportsError()
    {
        using var temp = CreateTempDirectory();
        var first = WriteSimpleToml(temp.Path, "first.toml", "asset.same", "First");
        var second = WriteSimpleToml(temp.Path, "second.toml", "asset.same", "Second");

        var result = TomlAssetPackLoader.LoadFiles<SimpleAsset>([first, second], asset => new AssetId(asset.Id));

        Assert.False(result.Success);
        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Code == "asset.duplicate_id"));
        Assert.Equal(AssetDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("asset.same", diagnostic.Message);
        Assert.Contains(first, diagnostic.Message);
        Assert.Contains(second, diagnostic.Message);
        Assert.Equal(second, diagnostic.SourcePath);
        Assert.NotNull(result.Pack);
        Assert.Single(result.Pack.Assets);
        Assert.Equal("First", result.Pack.Assets[new AssetId("asset.same")].Asset.Title);
    }

    [Fact]
    public void TomlAssetPackLoader_ContinueOnError_LoadsValidFilesAndReportsInvalidFile()
    {
        using var temp = CreateTempDirectory();
        var valid = WriteSimpleToml(temp.Path, "valid.toml", "asset.valid", "Valid");
        var invalid = Path.Combine(temp.Path, "invalid.toml");
        File.WriteAllText(invalid, "id = \"unterminated");

        var result = TomlAssetPackLoader.LoadFiles<SimpleAsset>([invalid, valid], asset => new AssetId(asset.Id));

        Assert.False(result.Success);
        Assert.NotNull(result.Pack);
        Assert.True(result.Pack.TryGet(new AssetId("asset.valid"), out _));
        Assert.Contains(result.Diagnostics, d => d.Code == "TOML_PARSE" && d.SourcePath == invalid);
    }

    [Fact]
    public void TomlAssetPackLoader_StopOnError_WhenContinueOnErrorFalse()
    {
        using var temp = CreateTempDirectory();
        var invalid = Path.Combine(temp.Path, "invalid.toml");
        File.WriteAllText(invalid, "id = \"unterminated");
        var valid = WriteSimpleToml(temp.Path, "valid.toml", "asset.valid", "Valid");

        var result = TomlAssetPackLoader.LoadFiles<SimpleAsset>(
            [invalid, valid],
            asset => new AssetId(asset.Id),
            options: new AssetPackLoadOptions { ContinueOnError = false });

        Assert.False(result.Success);
        Assert.NotNull(result.Pack);
        Assert.Empty(result.Pack.Assets);
        Assert.Contains(result.Diagnostics, d => d.Code == "TOML_PARSE" && d.SourcePath == invalid);
    }

    [Fact]
    public void AssetPack_TryGet_ReturnsAssetAndEntry()
    {
        var id = new AssetId("asset.one");
        var asset = new SimpleAsset { Id = id.ToString(), Title = "One", Score = 1 };
        var entry = new AssetPackEntry<SimpleAsset> { Id = id, Asset = asset, SourcePath = "one.toml" };
        var pack = new AssetPack<SimpleAsset> { Assets = new Dictionary<AssetId, AssetPackEntry<SimpleAsset>> { [id] = entry } };

        Assert.True(pack.TryGet(id, out var foundAsset));
        Assert.Same(asset, foundAsset);
        Assert.True(pack.TryGetEntry(id, out var foundEntry));
        Assert.Same(entry, foundEntry);
    }

    [Fact]
    public void AssetPackValidator_ReportsMissingCrossAssetReference()
    {
        var source = new SimpleAsset { Id = "asset.source", Title = "Source", Score = 0 };
        var pack = new AssetPack<SimpleAsset>
        {
            Assets = new Dictionary<AssetId, AssetPackEntry<SimpleAsset>>
            {
                [new AssetId(source.Id)] = new() { Id = new AssetId(source.Id), Asset = source, SourcePath = "source.toml" }
            }
        };

        var diagnostic = AssetPackValidation.MissingReference(pack, new AssetId("asset.missing"), "source.toml", "target");

        Assert.NotNull(diagnostic);
        Assert.Equal("asset.reference_missing", diagnostic.Code);
        Assert.Equal("source.toml", diagnostic.SourcePath);
    }

    [Fact]
    public void AriadneDialoguePack_LoadsMultipleDialogueAssets()
    {
        var result = LoadDialoguePack();

        Assert.True(result.Success, FormatDiagnostics(result.Diagnostics));
        Assert.NotNull(result.Pack);
        Assert.Equal(2, result.Pack.Assets.Count);
        Assert.True(result.Pack.TryGet(new AssetId("dialogue.blacksmith_intro"), out _));
        Assert.True(result.Pack.TryGet(new AssetId("dialogue.north_road_job"), out _));
    }

    [Fact]
    public void AriadneDialoguePack_ValidatesCrossAssetChoice()
    {
        var result = LoadDialoguePack();

        Assert.True(result.Success, FormatDiagnostics(result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code is "DIALOGUE_CHOICE_ASSET_MISSING" or "DIALOGUE_CHOICE_ASSET_NODE_MISSING");
    }

    [Fact]
    public void AriadneDialoguePack_RejectsMissingCrossAssetChoice()
    {
        using var temp = CreateTempDirectory();
        WriteDialogueToml(temp.Path, "source.toml", "dialogue.source", "dialogue.missing", "offer");

        var result = LoadDialoguePack(temp.Path);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "DIALOGUE_CHOICE_ASSET_MISSING" && d.SourcePath!.EndsWith("source.toml", StringComparison.Ordinal));
    }

    [Fact]
    public void AriadneDialoguePack_RejectsMissingCrossAssetNode()
    {
        using var temp = CreateTempDirectory();
        WriteDialogueToml(temp.Path, "source.toml", "dialogue.source", "dialogue.target", "missing");
        WriteDialogueToml(temp.Path, "target.toml", "dialogue.target");

        var result = LoadDialoguePack(temp.Path);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "DIALOGUE_CHOICE_ASSET_NODE_MISSING" && d.SourcePath!.EndsWith("source.toml", StringComparison.Ordinal));
    }

    [Fact]
    public void TomlIsData_CrossReferencesAreSymbolicOnly()
    {
        var result = LoadDialoguePack();

        Assert.True(result.Success, FormatDiagnostics(result.Diagnostics));
        var blacksmith = result.Pack!.Assets[new AssetId("dialogue.blacksmith_intro")].Asset;
        var crossAssetChoice = Assert.Single(blacksmith.Nodes["greeting"].Choices, choice => choice.Id == "ask_work");
        Assert.Equal("dialogue.north_road_job", crossAssetChoice.NextAsset);
        Assert.Equal("offer", crossAssetChoice.NextNode);
        Assert.IsType<string>(crossAssetChoice.NextAsset);
        var shop = blacksmith.Nodes["shop"];
        Assert.Equal("can_trade_with_blacksmith", shop.Condition);
        var effect = Assert.Single(shop.Effects);
        Assert.Equal("open_shop", effect.Id);
        Assert.Equal("blacksmith_basic", effect.Value);
    }

    private static TomlAssetLoadResult<DialogueAsset> LoadBlacksmith()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "dialogue", "blacksmith.toml");
        return TomlAssetLoader.LoadFile<DialogueAsset>(path, new DialogueAssetValidator());
    }

    private static AssetPackLoadResult<DialogueAsset> LoadDialoguePack(string? directory = null) =>
        TomlAssetPackLoader.LoadDirectory<DialogueAsset>(
            directory ?? Path.Combine(AppContext.BaseDirectory, "dialogue"),
            dialogue => new AssetId(dialogue.Id),
            new DialogueAssetValidator(),
            new DialogueAssetPackValidator());

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

    private static string WriteDialogueToml(string directory, string fileName, string id, string? nextAsset = null, string? nextNode = null)
    {
        var choiceTarget = nextAsset is null
            ? "next = \"end\""
            : $$"""
              next_asset = "{{nextAsset}}"
              next_node = "{{nextNode}}"
              """;

        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, $$"""
            id = "{{id}}"
            title = "Test Dialogue"
            start = "start"

            [nodes.start]
            speaker = "Narrator"
            text = "Start."

            [[nodes.start.choices]]
            id = "go"
            text = "Go."
            {{choiceTarget}}

            [nodes.end]
            speaker = "Narrator"
            text = "End."
            """);
        return path;
    }

    private static TempDirectory CreateTempDirectory() => new(Path.Combine(Path.GetTempPath(), $"dominatus-assets-{Guid.NewGuid():N}"));

    private static string FormatDiagnostics(IEnumerable<AssetDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(d => $"{d.Severity} {d.Code}: {d.Message} ({d.SourcePath})"));

    public sealed record SimpleAsset
    {
        public required string Id { get; init; }

        public required string Title { get; init; }

        public int Score { get; init; }
    }

    public sealed record ReferenceAsset
    {
        public required string Target { get; init; }
    }

    public sealed class ReferenceAssetValidator : IAssetValidator<ReferenceAsset>
    {
        public IReadOnlyList<AssetDiagnostic> Validate(ReferenceAsset asset, AssetValidationContext context)
        {
            return asset.Target == "known.node"
                ? []
                : [AssetValidation.Error("TEST_REFERENCE_MISSING", $"Target '{asset.Target}' does not exist.", context.SourcePath)];
        }
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
