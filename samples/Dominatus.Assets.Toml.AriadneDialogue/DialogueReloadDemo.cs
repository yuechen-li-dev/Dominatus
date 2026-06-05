using System.Text;
using Dominatus.Assets.Toml;

namespace Dominatus.Assets.Toml.AriadneDialogue;

public sealed record DialogueReloadDemoResult
{
    public required string TempDirectory { get; init; }

    public required AssetId EditedAssetId { get; init; }

    public required AssetPackReloadResult<DialogueAsset> ReloadResult { get; init; }

    public required DialogueRunResult TraversalResult { get; init; }

    public bool Success => ReloadResult.Success && TraversalResult.Success;
}

public static class DialogueReloadDemo
{
    public static DialogueReloadDemoResult Run(string sourceDialogueDirectory, ILocalizationTable localizationTable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDialogueDirectory);
        ArgumentNullException.ThrowIfNull(localizationTable);

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"dominatus-ariadne-reload-{Guid.NewGuid():N}");
        CopyDirectory(sourceDialogueDirectory, tempDirectory);

        var initialLoad = LoadDialoguePack(tempDirectory);
        if (!initialLoad.Success || initialLoad.Pack is null)
        {
            throw new InvalidOperationException($"Reload demo initial load failed: {AssetDiagnosticFormatter.FormatMany(initialLoad.Diagnostics)}");
        }

        var editedAssetId = new AssetId("dialogue.north_road_job");
        var editedFile = Path.Combine(tempDirectory, "quest_north_road.toml");
        var original = File.ReadAllText(editedFile);
        File.WriteAllText(
            editedFile,
            original.Replace("North Road Job", "North Road Job (Reloaded)", StringComparison.Ordinal));

        var reloadResult = TomlAssetPackReloader.ReloadDirectory(
            initialLoad.Pack,
            tempDirectory,
            dialogue => new AssetId(dialogue.Id),
            new DialogueAssetValidator(),
            new DialogueAssetPackValidator());
        var traversalResult = AriadneDialogueSampleRunner.RunScripted(reloadResult.EffectivePack, localizationTable);

        return new DialogueReloadDemoResult
        {
            TempDirectory = tempDirectory,
            EditedAssetId = editedAssetId,
            ReloadResult = reloadResult,
            TraversalResult = traversalResult
        };
    }

    public static string Format(DialogueReloadDemoResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var builder = new StringBuilder();
        builder.AppendLine("Reload demo:");
        builder.Append("Initial pack: ").Append(result.ReloadResult.OldPack.Assets.Count).AppendLine(" assets");
        builder.Append("Edited temp dialogue file: ").AppendLine(result.EditedAssetId.Value);
        builder.AppendLine(AssetPackReloadReportFormatter.Format(result.ReloadResult));
        builder.Append("Changed: ").AppendLine(result.ReloadResult.Changed.Count == 0 ? "<none>" : string.Join(", ", result.ReloadResult.Changed.Select(id => id.Value)));
        builder.Append("Effective pack: ").AppendLine(result.ReloadResult.UsedOldPack ? "old" : "new");
        builder.Append("Traversal: ").AppendLine(result.TraversalResult.Success ? "OK" : "FAILED");
        return builder.ToString().TrimEnd();
    }

    private static AssetPackLoadResult<DialogueAsset> LoadDialoguePack(string directory) =>
        TomlAssetPackLoader.LoadDirectory<DialogueAsset>(
            directory,
            dialogue => new AssetId(dialogue.Id),
            new DialogueAssetValidator(),
            new DialogueAssetPackValidator());

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*.toml", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }
}
