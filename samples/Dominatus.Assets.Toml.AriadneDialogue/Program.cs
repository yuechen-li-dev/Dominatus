using Dominatus.Assets.Toml;
using Dominatus.Assets.Toml.AriadneDialogue;

Console.WriteLine("Dominatus.Assets.Toml Ariadne Dialogue Sample");

var dialogueDirectory = Path.Combine(AppContext.BaseDirectory, "dialogue");
var localizationPath = Path.Combine(AppContext.BaseDirectory, "localization", "en.csv");

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

Console.Write(DialoguePreviewRenderer.Render(pack, localizationTable));

if (diagnostics.Count > 0)
{
    PrintDiagnostics(diagnostics);
}

static void PrintDiagnostics(IReadOnlyList<AssetDiagnostic> diagnostics)
{
    Console.WriteLine("Diagnostics:");
    Console.WriteLine(AssetDiagnosticFormatter.FormatMany(diagnostics));
}
