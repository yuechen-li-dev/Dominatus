using Dominatus.Assets.Toml;
using Dominatus.Assets.Toml.AriadneDialogue;

Console.WriteLine("Dominatus.Assets.Toml Ariadne Dialogue Sample");

var directory = Path.Combine(AppContext.BaseDirectory, "dialogue");
var result = TomlAssetPackLoader.LoadDirectory<DialogueAsset>(
    directory,
    dialogue => new AssetId(dialogue.Id),
    new DialogueAssetValidator(),
    new DialogueAssetPackValidator());

if (result.Pack is not { } pack)
{
    PrintDiagnostics(result.Diagnostics);
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine($"Loaded dialogue pack: {pack.Assets.Count} assets");
Console.WriteLine($"Validation: {(result.Success ? "OK" : "FAILED")}");
Console.WriteLine();

foreach (var entry in pack.Assets.Values.OrderBy(entry => entry.Id.ToString(), StringComparer.Ordinal))
{
    var dialogue = entry.Asset;
    Console.WriteLine(dialogue.Id);
    Console.WriteLine($"Title: {dialogue.Title}");
    Console.WriteLine($"Start: {dialogue.Start}");

    foreach (var (nodeId, node) in dialogue.Nodes)
    {
        Console.WriteLine($"[{nodeId}] {node.Speaker}: {node.Text}");
        if (!string.IsNullOrWhiteSpace(node.Condition))
        {
            Console.WriteLine($"condition: {node.Condition}");
        }

        foreach (var effect in node.Effects)
        {
            Console.WriteLine($"effect: {effect.Id}{(effect.Value is null ? string.Empty : $" = {effect.Value}")}");
        }

        foreach (var choice in node.Choices)
        {
            var target = string.IsNullOrWhiteSpace(choice.NextAsset)
                ? choice.Next
                : $"{choice.NextAsset}:{choice.NextNode}";
            Console.WriteLine($"-> {choice.Id}: {choice.Text} [{target}]");
        }
    }

    Console.WriteLine();
}

if (result.Diagnostics.Count > 0)
{
    PrintDiagnostics(result.Diagnostics);
}

static void PrintDiagnostics(IReadOnlyList<AssetDiagnostic> diagnostics)
{
    Console.WriteLine("Diagnostics:");
    Console.WriteLine(AssetDiagnosticFormatter.FormatMany(diagnostics));
}
