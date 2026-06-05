using Dominatus.Assets.Toml;
using Dominatus.Assets.Toml.AriadneDialogue;

namespace Dominatus.Assets.Toml.Tests;

public sealed class TomlAssetLoaderTests
{

    [Fact]
    public void AssetDiagnosticFormatter_FormatsPathLineColumnAndKeyPath()
    {
        var diagnostic = new AssetDiagnostic
        {
            Severity = AssetDiagnosticSeverity.Error,
            Code = "asset.missing_reference",
            Message = "Missing asset reference 'dialogue.missing'.",
            SourcePath = "dialogue/blacksmith.toml",
            Line = 12,
            Column = 5,
            Span = new AssetSourceSpan
            {
                SourcePath = "dialogue/blacksmith.toml",
                StartLine = 12,
                StartColumn = 5,
                EndLine = 12,
                EndColumn = 15
            },
            KeyPath = "nodes.greeting.choices[0].next_asset"
        };

        var formatted = AssetDiagnosticFormatter.Format(diagnostic);

        Assert.Equal($"error asset.missing_reference: Missing asset reference 'dialogue.missing'.{Environment.NewLine}at dialogue/blacksmith.toml:12:5{Environment.NewLine}key: nodes.greeting.choices[0].next_asset", formatted);
    }

    [Fact]
    public void DiagnosticFormatter_FormatMany_StableOrdering()
    {
        var first = AssetValidation.Warning("asset.first", "First.", "a.toml");
        var second = AssetValidation.Error("asset.second", "Second.", "b.toml", keyPath: "id");

        var formatted = AssetDiagnosticFormatter.FormatMany([first, second]);

        Assert.Equal($"warning asset.first: First.{Environment.NewLine}at a.toml{Environment.NewLine}error asset.second: Second.{Environment.NewLine}at b.toml{Environment.NewLine}key: id", formatted);
    }

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
        Assert.Contains(result.Diagnostics, d => d.Severity == AssetDiagnosticSeverity.Error && d.Code == "toml.parse");
    }

    [Fact]
    public void TomlAssetLoader_InvalidToml_DiagnosticHasSourceSpanOrLineColumn()
    {
        var result = TomlAssetLoader.LoadString<SimpleAsset>("id = \"unterminated", new TomlAssetLoadOptions { SourcePath = "broken.toml" });

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Code == "toml.parse"));
        Assert.Equal("broken.toml", diagnostic.SourcePath);
        Assert.True(
            diagnostic.Span is { StartLine: not null, StartColumn: not null } ||
            diagnostic is { Line: not null, Column: not null });
    }

    [Fact]
    public void TomlAssetLoader_BindError_DiagnosticHasSourcePathAndKeyPathWhenAvailable()
    {
        var result = TomlAssetLoader.LoadString<StrictAsset>("""
            id = "asset.one"
            count = "not a number"
            """, new TomlAssetLoadOptions { SourcePath = "strict.toml" });

        Assert.False(result.Success);
        var diagnostic = result.Diagnostics.First(d => d.Code == "toml.bind");
        Assert.Equal("strict.toml", diagnostic.SourcePath);
        Assert.True(string.IsNullOrWhiteSpace(diagnostic.KeyPath) || diagnostic.KeyPath == "count");
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

        Assert.Contains(diagnostics, d => d.Code == "dialogue.missing_start_node" && d.KeyPath == "start");
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

        Assert.Contains(diagnostics, d => d.Code == "dialogue.missing_choice_target" && d.KeyPath == "nodes.start.choices[0].next");
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
        Assert.Equal("id", diagnostic.KeyPath);
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
        Assert.Contains(result.Diagnostics, d => d.Code == "toml.parse" && d.SourcePath == invalid);
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
        Assert.Contains(result.Diagnostics, d => d.Code == "toml.parse" && d.SourcePath == invalid);
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
        Assert.Equal("asset.missing_reference", diagnostic.Code);
        Assert.Equal("source.toml", diagnostic.SourcePath);
        Assert.Equal("target", diagnostic.KeyPath);
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
        Assert.DoesNotContain(result.Diagnostics, d => d.Code is "dialogue.missing_choice_asset" or "dialogue.missing_choice_asset_node");
    }

    [Fact]
    public void AriadneDialoguePack_RejectsMissingCrossAssetChoice()
    {
        using var temp = CreateTempDirectory();
        WriteDialogueToml(temp.Path, "source.toml", "dialogue.source", "dialogue.missing", "offer");

        var result = LoadDialoguePack(temp.Path);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "dialogue.missing_choice_asset" && d.SourcePath!.EndsWith("source.toml", StringComparison.Ordinal) && d.KeyPath == "nodes.start.choices[0].next_asset");
    }

    [Fact]
    public void AriadneDialoguePack_RejectsMissingCrossAssetNode()
    {
        using var temp = CreateTempDirectory();
        WriteDialogueToml(temp.Path, "source.toml", "dialogue.source", "dialogue.target", "missing");
        WriteDialogueToml(temp.Path, "target.toml", "dialogue.target");

        var result = LoadDialoguePack(temp.Path);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Code == "dialogue.missing_choice_asset_node" && d.SourcePath!.EndsWith("source.toml", StringComparison.Ordinal) && d.KeyPath == "nodes.start.choices[0].next_node");
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


    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void LocalizationKey_RejectsEmptyOrWhitespace(string value)
    {
        Assert.Throws<ArgumentException>(() => new LocalizationKey(value));
    }

    [Fact]
    public void DictionaryLocalizationTable_ContainsAndTryGet()
    {
        var key = new LocalizationKey("dialogue.blacksmith_intro.greeting");
        var table = new DictionaryLocalizationTable(new Dictionary<LocalizationKey, string> { [key] = "Hello" });

        Assert.True(table.Contains(key));
        Assert.True(table.TryGet(key, out var value));
        Assert.Equal("Hello", value);
        Assert.False(table.Contains(new LocalizationKey("dialogue.missing")));
    }

    [Fact]
    public void LocalizationValidation_MissingKey_ReturnsDiagnosticWithKeyPathAndSpan()
    {
        var span = new AssetSourceSpan { SourcePath = "dialogue.toml", StartLine = 4, StartColumn = 1, EndLine = 4, EndColumn = 5 };
        var table = new DictionaryLocalizationTable(new Dictionary<LocalizationKey, string>());

        var diagnostic = LocalizationValidation.MissingLocalizationKey(table, new LocalizationKey("dialogue.missing"), "dialogue.toml", "nodes.start.line", span);

        Assert.NotNull(diagnostic);
        Assert.Equal("localization.missing_key", diagnostic.Code);
        Assert.Equal("dialogue.toml", diagnostic.SourcePath);
        Assert.Equal("nodes.start.line", diagnostic.KeyPath);
        Assert.Same(span, diagnostic.Span);
        Assert.Equal(4, diagnostic.Line);
        Assert.Equal(1, diagnostic.Column);
    }

    [Fact]
    public void DialogueLocalizationValidator_AcceptsKeysPresentInTable()
    {
        var result = LoadDialoguePack();
        var table = LoadSampleLocalizationTable();

        var diagnostics = new DialogueLocalizationValidator(table).Validate(result.Pack!, new AssetValidationContext());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void DialogueLocalizationValidator_RejectsMissingNodeLineKey()
    {
        var result = TomlAssetLoader.LoadString<DialogueAsset>("""
            id = "dialogue.test"
            title = "Test"
            start = "start"

            [nodes.start]
            speaker = "narrator"
            line = "dialogue.test.missing"
            text = "Fallback."
            """, new DialogueAssetValidator(), new TomlAssetLoadOptions { SourcePath = "dialogue.toml" });
        var pack = ToPack(result.Value!, "dialogue.toml", result.SourceMap);
        var table = new DictionaryLocalizationTable(new Dictionary<LocalizationKey, string>());

        var diagnostics = new DialogueLocalizationValidator(table).Validate(pack, new AssetValidationContext());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("localization.missing_key", diagnostic.Code);
        Assert.Equal("nodes.start.line", diagnostic.KeyPath);
        Assert.Equal("dialogue.toml", diagnostic.SourcePath);
        Assert.NotNull(diagnostic.Span);
    }

    [Fact]
    public void DialogueLocalizationValidator_RejectsMissingChoiceLineKey()
    {
        var result = TomlAssetLoader.LoadString<DialogueAsset>("""
            id = "dialogue.test"
            title = "Test"
            start = "start"

            [nodes.start]
            speaker = "narrator"
            line = "dialogue.test.start"
            text = "Fallback."

            [[nodes.start.choices]]
            id = "go"
            line = "choice.test.missing"
            text = "Go."
            next = "end"

            [nodes.end]
            speaker = "narrator"
            line = "dialogue.test.end"
            text = "End."
            """, new DialogueAssetValidator(), new TomlAssetLoadOptions { SourcePath = "dialogue.toml" });
        var pack = ToPack(result.Value!, "dialogue.toml", result.SourceMap);
        var table = new DictionaryLocalizationTable(new Dictionary<LocalizationKey, string>
        {
            [new LocalizationKey("dialogue.test.start")] = "Start.",
            [new LocalizationKey("dialogue.test.end")] = "End."
        });

        var diagnostics = new DialogueLocalizationValidator(table).Validate(pack, new AssetValidationContext());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("localization.missing_key", diagnostic.Code);
        Assert.Equal("nodes.start.choices[0].line", diagnostic.KeyPath);
        Assert.NotNull(diagnostic.Span);
    }

    [Fact]
    public void DialogueLocalizationValidator_WarnsInlineTextWithoutLine()
    {
        var dialogue = new DialogueAsset
        {
            Id = "dialogue.test",
            Title = "Test",
            Start = "start",
            Nodes = new Dictionary<string, DialogueNodeAsset>
            {
                ["start"] = new() { Speaker = "Narrator", Text = "Inline." }
            }
        };

        var diagnostics = new DialogueAssetValidator().Validate(dialogue, new AssetValidationContext { SourcePath = "inline" });

        Assert.Contains(diagnostics, d => d.Code == "dialogue.inline_text_only" && d.Severity == AssetDiagnosticSeverity.Warning && d.KeyPath == "nodes.start.text");
    }

    [Fact]
    public void DialogueLocalizationValidator_ErrorsWhenLineAndTextMissing()
    {
        var dialogue = new DialogueAsset
        {
            Id = "dialogue.test",
            Title = "Test",
            Start = "start",
            Nodes = new Dictionary<string, DialogueNodeAsset>
            {
                ["start"] = new() { Speaker = "Narrator" }
            }
        };

        var diagnostics = new DialogueAssetValidator().Validate(dialogue, new AssetValidationContext { SourcePath = "inline" });

        Assert.Contains(diagnostics, d => d.Code == "dialogue.missing_line_or_text" && d.Severity == AssetDiagnosticSeverity.Error && d.KeyPath == "nodes.start.line");
    }

    [Fact]
    public void AriadneDialogueSample_LoadsLocalizationTable()
    {
        var table = LoadSampleLocalizationTable();

        Assert.Equal(10, table.Count);
        Assert.True(table.TryGet(new LocalizationKey("dialogue.blacksmith_intro.greeting"), out var text));
        Assert.Equal("You look like someone who needs a blade.", text);
    }

    [Fact]
    public void AriadneDialogueSample_ValidatesLocalizationKeys()
    {
        var result = LoadDialoguePack();
        var table = LoadSampleLocalizationTable();

        var diagnostics = new DialogueLocalizationValidator(table).Validate(result.Pack!, new AssetValidationContext());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void AriadneDialogueSample_LocalizedPreviewUsesTableText()
    {
        var result = LoadDialoguePack();
        var table = LoadSampleLocalizationTable();

        var preview = DialoguePreviewRenderer.Render(result.Pack!, table);

        Assert.Contains("[greeting] blacksmith: You look like someone who needs a blade. (dialogue.blacksmith_intro.greeting)", preview);
        Assert.Contains("-> accept: I'll look into it. (choice.north_road_job.accept) [end]", preview);
    }

    [Fact]
    public void TomlIsData_LocalizationKeysAreDataOnly()
    {
        var result = LoadDialoguePack();
        var blacksmith = result.Pack!.Assets[new AssetId("dialogue.blacksmith_intro")].Asset;
        var greeting = blacksmith.Nodes["greeting"];
        var table = new DictionaryLocalizationTable(new Dictionary<LocalizationKey, string>
        {
            [new LocalizationKey(greeting.Line!)] = "Localized greeting from table."
        });

        Assert.Equal("dialogue.blacksmith_intro.greeting", greeting.Line);
        Assert.Equal("You look like someone who needs a blade.", greeting.Text);
        Assert.True(table.TryGet(new LocalizationKey(greeting.Line!), out var localized));
        Assert.Equal("Localized greeting from table.", localized);
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


    private static DictionaryLocalizationTable LoadSampleLocalizationTable()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "localization", "en.csv");
        var result = SampleLocalizationCsvLoader.LoadFile(path);
        Assert.True(result.Success, FormatDiagnostics(result.Diagnostics));
        Assert.NotNull(result.Value);
        return result.Value;
    }

    private static AssetPack<DialogueAsset> ToPack(DialogueAsset asset, string sourcePath, TomlAssetSourceMap? sourceMap) =>
        new()
        {
            Assets = new Dictionary<AssetId, AssetPackEntry<DialogueAsset>>
            {
                [new AssetId(asset.Id)] = new() { Id = new AssetId(asset.Id), Asset = asset, SourcePath = sourcePath, SourceMap = sourceMap }
            }
        };

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
        AssetDiagnosticFormatter.FormatMany(diagnostics);

    public sealed record SimpleAsset
    {
        public required string Id { get; init; }

        public required string Title { get; init; }

        public int Score { get; init; }
    }

    public sealed record StrictAsset
    {
        public required string Id { get; init; }

        public required int Count { get; init; }
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
                : [AssetValidation.Error("TEST_REFERENCE_MISSING", $"Target '{asset.Target}' does not exist.", context.SourcePath, keyPath: "target", span: context.GetSpan("target"))];
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
