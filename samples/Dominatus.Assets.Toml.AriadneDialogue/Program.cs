using Dominatus.Assets.Toml;
using Dominatus.Assets.Toml.AriadneDialogue;

Console.WriteLine("Dominatus.Assets.Toml Ariadne Dialogue Sample");

var path = Path.Combine(AppContext.BaseDirectory, "dialogue", "blacksmith.toml");
var result = TomlAssetLoader.LoadFile<DialogueAsset>(path, new DialogueAssetValidator());

if (result.Value is not { } dialogue)
{
    PrintDiagnostics(result.Diagnostics);
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine($"Loaded: {dialogue.Id}");
Console.WriteLine($"Title: {dialogue.Title}");
Console.WriteLine($"Start: {dialogue.Start}");
Console.WriteLine($"Nodes: {dialogue.Nodes.Count}");
Console.WriteLine($"Validation: {(result.Success ? "OK" : "FAILED")}");
Console.WriteLine();

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
        Console.WriteLine($"-> {choice.Id}: {choice.Text} [{choice.Next}]");
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
    foreach (var diagnostic in diagnostics)
    {
        var location = diagnostic.Line is null
            ? diagnostic.SourcePath
            : $"{diagnostic.SourcePath}:{diagnostic.Line}:{diagnostic.Column}";
        Console.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}{(location is null ? string.Empty : $" ({location})")}");
    }
}
