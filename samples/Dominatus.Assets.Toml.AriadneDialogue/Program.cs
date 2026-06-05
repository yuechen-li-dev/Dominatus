using Dominatus.Assets.Toml;
using Dominatus.Assets.Toml.AriadneDialogue;

Console.WriteLine("Dominatus.Assets.Toml Ariadne Dialogue Runtime Bridge");

var dialogueDirectory = Path.Combine(AppContext.BaseDirectory, "dialogue");
var localizationPath = Path.Combine(AppContext.BaseDirectory, "localization", "en.csv");
var scripted = !args.Contains("--interactive", StringComparer.OrdinalIgnoreCase);
var reloadDemo = args.Contains("--reload-demo", StringComparer.OrdinalIgnoreCase);
var start = ParseStart(args) ?? new DialogueAddress(new AssetId("dialogue.blacksmith_intro"), "greeting");

var packResult = TomlAssetPackLoader.LoadDirectory<DialogueAsset>(
    dialogueDirectory,
    dialogue => new AssetId(dialogue.Id),
    new DialogueAssetValidator(),
    new DialogueAssetPackValidator());

var localizationResult = SampleLocalizationCsvLoader.LoadFile(localizationPath);
var diagnostics = new List<AssetDiagnostic>();
diagnostics.AddRange(packResult.Diagnostics);
diagnostics.AddRange(localizationResult.Diagnostics);

if (packResult.Pack is not { } pack || localizationResult.Value is not { } localizationTable)
{
    PrintDiagnostics(diagnostics);
    Environment.ExitCode = 1;
    return;
}

diagnostics.AddRange(new DialogueLocalizationValidator(localizationTable).Validate(pack, new AssetValidationContext()));

Console.WriteLine($"Loaded dialogue pack: {pack.Assets.Count} assets");
Console.WriteLine($"Loaded localization keys: {localizationTable.Count}");
Console.WriteLine($"Validation: {(diagnostics.Any(d => d.Severity == AssetDiagnosticSeverity.Error) ? "FAILED" : "OK")}");
Console.WriteLine();

if (args.Contains("--preview", StringComparer.OrdinalIgnoreCase))
{
    Console.Write(DialoguePreviewRenderer.Render(pack, localizationTable));
}

if (reloadDemo && !diagnostics.Any(d => d.Severity == AssetDiagnosticSeverity.Error))
{
    var reloadDemoResult = DialogueReloadDemo.Run(dialogueDirectory, localizationTable);
    Console.WriteLine(DialogueReloadDemo.Format(reloadDemoResult));
    Console.WriteLine();
    pack = reloadDemoResult.ReloadResult.EffectivePack;
    diagnostics.AddRange(reloadDemoResult.ReloadResult.Diagnostics);
    diagnostics.AddRange(reloadDemoResult.TraversalResult.Diagnostics);
}

if (diagnostics.Any(d => d.Severity == AssetDiagnosticSeverity.Error))
{
    PrintDiagnostics(diagnostics);
    Environment.ExitCode = 1;
    return;
}

if (!scripted)
{
    Console.WriteLine("Interactive mode is intentionally not implemented for M4; running deterministic scripted traversal.");
}

var result = AriadneDialogueSampleRunner.RunScripted(pack, localizationTable, start, ["ask_work", "accept"]);
PrintRun(start, result);

if (diagnostics.Count > 0 || result.Diagnostics.Count > 0)
{
    PrintDiagnostics(diagnostics.Concat(result.Diagnostics).ToList());
}

Environment.ExitCode = result.Success ? 0 : 1;

static DialogueAddress? ParseStart(string[] args)
{
    var index = Array.IndexOf(args, "--start");
    if (index < 0 || index + 1 >= args.Length)
    {
        return null;
    }

    return DialogueAddress.Parse(args[index + 1]);
}

static void PrintRun(DialogueAddress start, DialogueRunResult result)
{
    Console.WriteLine($"Start: {start}");
    Console.WriteLine();

    var lineIndex = 0;
    var presentedIndex = 0;
    var takenIndex = 0;
    var effectsPrinted = 0;

    foreach (var line in result.Lines)
    {
        Console.WriteLine(line);
        while (effectsPrinted < result.EffectsRun.Count && lineIndex == 1)
        {
            Console.WriteLine($"Effect: {result.EffectsRun[effectsPrinted++]}");
        }

        var choicesForNode = result.ChoicesPresented.Skip(presentedIndex).Take(lineIndex == 0 ? 2 : lineIndex == 1 ? 2 : 0).ToList();
        for (var i = 0; i < choicesForNode.Count; i++)
        {
            var choice = choicesForNode[i];
            var separator = choice.IndexOf(": ", StringComparison.Ordinal);
            Console.WriteLine($"{i + 1}. {(separator >= 0 ? choice[(separator + 2)..] : choice)}");
        }

        presentedIndex += choicesForNode.Count;
        if (takenIndex < result.ChoicesTaken.Count && choicesForNode.Count > 0)
        {
            Console.WriteLine($"   Chosen: {result.ChoicesTaken[takenIndex++]}");
        }

        Console.WriteLine();
        lineIndex++;
    }

    while (effectsPrinted < result.EffectsRun.Count)
    {
        Console.WriteLine($"Effect: {result.EffectsRun[effectsPrinted++]}");
    }

    Console.WriteLine("Traversal complete.");
}

static void PrintDiagnostics(IReadOnlyList<AssetDiagnostic> diagnostics)
{
    if (diagnostics.Count == 0)
    {
        return;
    }

    Console.WriteLine("Diagnostics:");
    Console.WriteLine(AssetDiagnosticFormatter.FormatMany(diagnostics));
}
