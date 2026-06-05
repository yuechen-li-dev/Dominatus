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
    public void TomlIsData_NotExecutable()
    {
        var result = LoadBlacksmith();

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
        var workOffer = result.Value!.Nodes["work_offer"];
        Assert.Equal("can_accept_bandit_quest", workOffer.Condition);
        var effect = Assert.Single(workOffer.Effects);
        Assert.Equal("offer_quest", effect.Id);
        Assert.Equal("north_road_bandits", effect.Value);
    }

    private static TomlAssetLoadResult<DialogueAsset> LoadBlacksmith()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "dialogue", "blacksmith.toml");
        return TomlAssetLoader.LoadFile<DialogueAsset>(path, new DialogueAssetValidator());
    }

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
}
