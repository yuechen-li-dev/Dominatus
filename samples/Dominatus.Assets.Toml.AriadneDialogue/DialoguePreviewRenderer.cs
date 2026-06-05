using Dominatus.Assets.Toml;

namespace Dominatus.Assets.Toml.AriadneDialogue;

public static class DialoguePreviewRenderer
{
    public static string Render(AssetPack<DialogueAsset> pack, ILocalizationTable table)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(table);

        var writer = new StringWriter();
        foreach (var entry in pack.Assets.Values.OrderBy(entry => entry.Id.ToString(), StringComparer.Ordinal))
        {
            var dialogue = entry.Asset;
            writer.WriteLine(dialogue.Id);
            writer.WriteLine($"Title: {dialogue.Title}");
            writer.WriteLine($"Start: {dialogue.Start}");

            foreach (var (nodeId, node) in dialogue.Nodes)
            {
                writer.WriteLine($"[{nodeId}] {node.Speaker}: {Resolve(node.Line, node.Text, table)}");
                if (!string.IsNullOrWhiteSpace(node.Condition))
                {
                    writer.WriteLine($"condition: {node.Condition}");
                }

                foreach (var effect in node.Effects)
                {
                    writer.WriteLine($"effect: {effect.Id}{(effect.Value is null ? string.Empty : $" = {effect.Value}")}");
                }

                foreach (var choice in node.Choices)
                {
                    var target = string.IsNullOrWhiteSpace(choice.NextAsset)
                        ? choice.Next
                        : $"{choice.NextAsset}:{choice.NextNode}";
                    writer.WriteLine($"-> {choice.Id}: {Resolve(choice.Line, choice.Text, table)} [{target}]");
                }
            }

            writer.WriteLine();
        }

        return writer.ToString();
    }

    private static string Resolve(string? line, string? fallbackText, ILocalizationTable table)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            var key = new LocalizationKey(line);
            if (table.TryGet(key, out var value))
            {
                return $"{value} ({key})";
            }

            return string.IsNullOrWhiteSpace(fallbackText)
                ? $"<missing {key}>"
                : $"{fallbackText} (fallback for missing {key})";
        }

        return fallbackText ?? "<missing text>";
    }
}
