using Ariadne.OptFlow.Commands;
using Dominatus.Assets.Toml.AriadneDialogue;

namespace Dominatus.Assets.Toml.Tests;

public sealed class AriadneDialogueRuntimeBridgeTests
{
    [Fact]
    public void AriadneBridge_InspectionResult_Documented()
    {
        // Ariadne.OptFlow currently exposes dialogue actuation primitives (Diag.Line/Choose/Ask)
        // and DiagChoice, while traversal is authored as C# HFSM state delegates. The TOML bridge
        // therefore builds a sample-local Ariadne-shaped graph and exposes choices as DiagChoice
        // values instead of inventing executable TOML or a dialogue VM.
        var choice = new DialogueRuntimeChoice
        {
            Id = "ask_work",
            Line = new DialogueRuntimeLine("blacksmith", new LocalizationKey("choice.blacksmith_intro.ask_work"), "Got any work?", "Got any work?"),
            Target = new DialogueAddress(new AssetId("dialogue.north_road_job"), "offer"),
            Effects = []
        };

        Assert.IsType<DiagChoice>(choice.ToAriadneChoice());
        Assert.Equal("ask_work", choice.ToAriadneChoice().Key);
    }

    [Fact]
    public void AriadneBridge_BuildsRuntimeGraphFromDialoguePack()
    {
        var (pack, table) = LoadSample();
        var graph = DialogueRuntimeBridge.BuildGraph(pack, table);

        Assert.Equal(2, graph.Starts.Count);
        Assert.True(graph.TryGetNode(new DialogueAddress(new AssetId("dialogue.blacksmith_intro"), "greeting"), out _));
        Assert.True(graph.TryGetNode(new DialogueAddress(new AssetId("dialogue.north_road_job"), "offer"), out _));
    }

    [Fact]
    public void AriadneBridge_ResolvesLocalChoiceTarget()
    {
        var (pack, table) = LoadSample();
        var graph = DialogueRuntimeBridge.BuildGraph(pack, table);

        var node = graph.Nodes[new DialogueAddress(new AssetId("dialogue.blacksmith_intro"), "greeting")];
        var browse = Assert.Single(node.Choices, choice => choice.Id == "browse");

        Assert.Equal(new DialogueAddress(new AssetId("dialogue.blacksmith_intro"), "shop"), browse.Target);
    }

    [Fact]
    public void AriadneBridge_ResolvesCrossAssetChoiceTarget()
    {
        var (pack, table) = LoadSample();
        var graph = DialogueRuntimeBridge.BuildGraph(pack, table);

        var node = graph.Nodes[new DialogueAddress(new AssetId("dialogue.blacksmith_intro"), "greeting")];
        var askWork = Assert.Single(node.Choices, choice => choice.Id == "ask_work");

        Assert.Equal(new DialogueAddress(new AssetId("dialogue.north_road_job"), "offer"), askWork.Target);
    }

    [Fact]
    public void AriadneBridge_ResolvesLocalizedLineText()
    {
        var (pack, table) = LoadSample();
        var graph = DialogueRuntimeBridge.BuildGraph(pack, table);

        var node = graph.Nodes[new DialogueAddress(new AssetId("dialogue.blacksmith_intro"), "greeting")];

        Assert.Equal("You look like someone who needs a blade.", node.Line.Text);
        Assert.Equal(new LocalizationKey("dialogue.blacksmith_intro.greeting"), node.Line.Key);
    }

    [Fact]
    public void AriadneBridge_UsesFallbackTextWhenLocalizationMissingIfAllowed()
    {
        var pack = Pack(new DialogueAsset
        {
            Id = "dialogue.fallback",
            Title = "Fallback",
            Start = "start",
            Nodes = new Dictionary<string, DialogueNodeAsset>
            {
                ["start"] = new() { Speaker = "narrator", Line = "missing.line", Text = "Fallback line" }
            }
        });
        var table = new DictionaryLocalizationTable(new Dictionary<LocalizationKey, string>());
        var diagnostics = new List<AssetDiagnostic>();

        var graph = DialogueRuntimeBridge.BuildGraph(pack, table, diagnostics);
        var node = graph.Nodes[new DialogueAddress(new AssetId("dialogue.fallback"), "start")];

        Assert.Equal("Fallback line", node.Line.Text);
        Assert.Contains(diagnostics, d => d.Code == "dialogue.localization_fallback" && d.Severity == AssetDiagnosticSeverity.Warning);
    }

    [Fact]
    public void AriadneBridge_EvaluatesConditionRegistry()
    {
        var (pack, table) = LoadSample();
        var graph = DialogueRuntimeBridge.BuildGraph(pack, table);
        var conditions = AriadneDialogueSampleRunner.CreateDefaultConditions();
        conditions.Register("can_trade_with_blacksmith", _ => false);
        var traversal = new DialogueTraversal(graph, conditions, AriadneDialogueSampleRunner.CreateDefaultEffects());

        var falseResult = traversal.RunScripted(new DialogueAddress(new AssetId("dialogue.blacksmith_intro"), "greeting"), ["browse"]);
        Assert.DoesNotContain(falseResult.ChoicesTaken, choice => choice == "browse");
        Assert.Contains(falseResult.Diagnostics, d => d.Code == "dialogue.scripted_choice_unavailable");

        conditions.Register("can_trade_with_blacksmith", _ => true);
        var trueResult = new DialogueTraversal(graph, conditions, AriadneDialogueSampleRunner.CreateDefaultEffects())
            .RunScripted(new DialogueAddress(new AssetId("dialogue.blacksmith_intro"), "greeting"), ["browse", "leave"]);
        Assert.Contains("browse", trueResult.ChoicesTaken);
    }

    [Fact]
    public void AriadneBridge_ReportsUnknownConditionSymbol()
    {
        var asset = new DialogueAsset
        {
            Id = "dialogue.unknown_condition",
            Title = "Unknown Condition",
            Start = "start",
            Nodes = new Dictionary<string, DialogueNodeAsset>
            {
                ["start"] = new() { Speaker = "narrator", Text = "Hello", Condition = "missing_condition" }
            }
        };

        var diagnostics = DialogueRuntimeBridge.ValidateRegistrySymbols(
            Pack(asset),
            new DialogueConditionRegistry(),
            new DialogueEffectRegistry());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("dialogue.unknown_condition", diagnostic.Code);
        Assert.Equal("nodes.start.condition", diagnostic.KeyPath);
    }

    [Fact]
    public void AriadneBridge_RunsEffectRegistry()
    {
        var (pack, table) = LoadSample();
        var graph = DialogueRuntimeBridge.BuildGraph(pack, table);
        var effects = new DialogueEffectRegistry();
        var log = new List<string>();
        effects.Register("offer_quest", (_, value) => log.Add($"offer quest {value}"));
        effects.Register("open_shop", (_, value) => log.Add($"open shop {value}"));

        var result = new DialogueTraversal(graph, AriadneDialogueSampleRunner.CreateDefaultConditions(), effects)
            .RunScripted(new DialogueAddress(new AssetId("dialogue.blacksmith_intro"), "greeting"), ["ask_work", "accept"]);

        Assert.Contains("offer_quest north_road_bandits", result.EffectsRun);
        Assert.Contains("offer quest north_road_bandits", log);
    }

    [Fact]
    public void AriadneBridge_ReportsUnknownEffectSymbol()
    {
        var asset = new DialogueAsset
        {
            Id = "dialogue.unknown_effect",
            Title = "Unknown Effect",
            Start = "start",
            Nodes = new Dictionary<string, DialogueNodeAsset>
            {
                ["start"] = new()
                {
                    Speaker = "narrator",
                    Text = "Hello",
                    Effects = [new DialogueEffectAsset { Id = "missing_effect", Value = "x" }]
                }
            }
        };

        var diagnostics = DialogueRuntimeBridge.ValidateRegistrySymbols(
            Pack(asset),
            new DialogueConditionRegistry(),
            new DialogueEffectRegistry());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("dialogue.unknown_effect", diagnostic.Code);
        Assert.Equal("nodes.start.effects[0].id", diagnostic.KeyPath);
    }

    [Fact]
    public void AriadneBridge_ScriptedPlaythrough_CrossesAssetsAndCompletes()
    {
        var (pack, table) = LoadSample();

        var result = AriadneDialogueSampleRunner.RunScripted(pack, table);

        Assert.True(result.Success, AssetDiagnosticFormatter.FormatMany(result.Diagnostics));
        Assert.Contains("blacksmith: You look like someone who needs a blade.", result.Lines);
        Assert.Contains("ask_work", result.ChoicesTaken);
        Assert.Contains("offer_quest north_road_bandits", result.EffectsRun);
        Assert.Equal(new DialogueAddress(new AssetId("dialogue.north_road_job"), "end"), result.FinalAddress);
    }

    [Fact]
    public void TomlStillData_NoExpressionExecution()
    {
        var asset = new DialogueAsset
        {
            Id = "dialogue.data_only",
            Title = "Data Only",
            Start = "start",
            Nodes = new Dictionary<string, DialogueNodeAsset>
            {
                ["start"] = new()
                {
                    Speaker = "narrator",
                    Text = "Hello",
                    Condition = "1 == 1",
                    Effects = [new DialogueEffectAsset { Id = "ctx.State[\"pwned\"] = true" }]
                }
            }
        };

        var diagnostics = DialogueRuntimeBridge.ValidateRegistrySymbols(
            Pack(asset),
            new DialogueConditionRegistry(),
            new DialogueEffectRegistry());

        Assert.Contains(diagnostics, d => d.Code == "dialogue.unknown_condition" && d.Message.Contains("1 == 1", StringComparison.Ordinal));
        Assert.Contains(diagnostics, d => d.Code == "dialogue.unknown_effect" && d.Message.Contains("ctx.State", StringComparison.Ordinal));
    }

    private static (AssetPack<DialogueAsset> Pack, DictionaryLocalizationTable Table) LoadSample()
    {
        var dialogueDirectory = Path.Combine(AppContext.BaseDirectory, "dialogue");
        var localizationPath = Path.Combine(AppContext.BaseDirectory, "localization", "en.csv");
        var packResult = TomlAssetPackLoader.LoadDirectory<DialogueAsset>(
            dialogueDirectory,
            dialogue => new AssetId(dialogue.Id),
            new DialogueAssetValidator(),
            new DialogueAssetPackValidator());
        var localizationResult = SampleLocalizationCsvLoader.LoadFile(localizationPath);

        Assert.True(packResult.Success, AssetDiagnosticFormatter.FormatMany(packResult.Diagnostics));
        Assert.True(localizationResult.Success, AssetDiagnosticFormatter.FormatMany(localizationResult.Diagnostics));
        return (packResult.Pack!, localizationResult.Value!);
    }

    private static AssetPack<DialogueAsset> Pack(DialogueAsset asset)
    {
        var id = new AssetId(asset.Id);
        return new AssetPack<DialogueAsset>
        {
            Assets = new Dictionary<AssetId, AssetPackEntry<DialogueAsset>>
            {
                [id] = new AssetPackEntry<DialogueAsset>
                {
                    Id = id,
                    Asset = asset,
                    SourcePath = $"{asset.Id}.toml"
                }
            }
        };
    }
}
