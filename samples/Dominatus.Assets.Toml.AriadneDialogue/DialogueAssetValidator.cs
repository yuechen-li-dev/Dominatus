using Dominatus.Assets.Toml;

namespace Dominatus.Assets.Toml.AriadneDialogue;

public sealed class DialogueAssetValidator : IAssetValidator<DialogueAsset>
{
    public IReadOnlyList<AssetDiagnostic> Validate(DialogueAsset asset, AssetValidationContext context)
    {
        var diagnostics = new List<AssetDiagnostic>();

        if (string.IsNullOrWhiteSpace(asset.Id))
        {
            diagnostics.Add(Required("id", context));
        }

        if (string.IsNullOrWhiteSpace(asset.Start))
        {
            diagnostics.Add(Required("start", context));
        }

        if (asset.Nodes is null || asset.Nodes.Count == 0)
        {
            diagnostics.Add(Error("dialogue.missing_node", "Dialogue must contain at least one node.", "nodes", context));
            return diagnostics;
        }

        if (!string.IsNullOrWhiteSpace(asset.Start) && !asset.Nodes.ContainsKey(asset.Start))
        {
            diagnostics.Add(Error("dialogue.missing_start_node", $"Start node '{asset.Start}' does not exist.", "start", context));
        }

        foreach (var (nodeId, node) in asset.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Speaker))
            {
                diagnostics.Add(Required($"nodes.{nodeId}.speaker", context));
            }

            if (string.IsNullOrWhiteSpace(node.Text))
            {
                diagnostics.Add(Required($"nodes.{nodeId}.text", context));
            }

            var choiceIds = new HashSet<string>(StringComparer.Ordinal);
            for (var choiceIndex = 0; choiceIndex < node.Choices.Count; choiceIndex++)
            {
                ValidateChoice(asset, nodeId, node.Choices[choiceIndex], choiceIndex, choiceIds, context, diagnostics);
            }
        }

        return diagnostics;
    }

    private static void ValidateChoice(
        DialogueAsset asset,
        string nodeId,
        DialogueChoiceAsset choice,
        int choiceIndex,
        HashSet<string> choiceIds,
        AssetValidationContext context,
        List<AssetDiagnostic> diagnostics)
    {
        var choicePath = $"nodes.{nodeId}.choices[{choiceIndex}]";

        if (string.IsNullOrWhiteSpace(choice.Id))
        {
            diagnostics.Add(Required($"{choicePath}.id", context));
        }
        else if (!choiceIds.Add(choice.Id))
        {
            diagnostics.Add(Error("dialogue.duplicate_choice_id", $"Node '{nodeId}' contains duplicate choice id '{choice.Id}'.", $"nodes.{nodeId}.choices", context));
        }

        if (string.IsNullOrWhiteSpace(choice.Text))
        {
            diagnostics.Add(Required($"{choicePath}.text", context));
        }

        var hasLocalNext = !string.IsNullOrWhiteSpace(choice.Next);
        var hasNextAsset = !string.IsNullOrWhiteSpace(choice.NextAsset);
        var hasNextNode = !string.IsNullOrWhiteSpace(choice.NextNode);

        if (hasLocalNext && (hasNextAsset || hasNextNode))
        {
            diagnostics.Add(Error("dialogue.choice_target_ambiguous", $"Choice '{choice.Id}' on node '{nodeId}' cannot set both next and next_asset/next_node.", choicePath, context));
        }

        if (hasLocalNext)
        {
            if (!asset.Nodes.ContainsKey(choice.Next!))
            {
                diagnostics.Add(Error("dialogue.missing_choice_target", $"Choice '{choice.Id}' on node '{nodeId}' points to missing node '{choice.Next}'.", $"{choicePath}.next", context));
            }

            return;
        }

        if (hasNextAsset)
        {
            if (!hasNextNode)
            {
                diagnostics.Add(Required($"{choicePath}.next_node", context));
            }

            return;
        }

        diagnostics.Add(Required($"{choicePath}.next", context));
    }

    private static AssetDiagnostic Required(string keyPath, AssetValidationContext context) =>
        AssetValidation.Error(
            "dialogue.required_field",
            $"Required field '{keyPath}' is missing or empty.",
            context.SourcePath,
            keyPath: keyPath,
            span: context.GetSpan(keyPath));

    private static AssetDiagnostic Error(string code, string message, string keyPath, AssetValidationContext context) =>
        AssetValidation.Error(code, message, context.SourcePath, keyPath: keyPath, span: context.GetSpan(keyPath));
}

public sealed class DialogueAssetPackValidator : IAssetPackValidator<DialogueAsset>
{
    public IReadOnlyList<AssetDiagnostic> Validate(AssetPack<DialogueAsset> pack, AssetValidationContext context)
    {
        var diagnostics = new List<AssetDiagnostic>();

        foreach (var entry in pack.Assets.Values)
        {
            foreach (var (nodeId, node) in entry.Asset.Nodes)
            {
                for (var choiceIndex = 0; choiceIndex < node.Choices.Count; choiceIndex++)
                {
                    var choice = node.Choices[choiceIndex];
                    if (string.IsNullOrWhiteSpace(choice.NextAsset))
                    {
                        continue;
                    }

                    var nextAssetPath = $"nodes.{nodeId}.choices[{choiceIndex}].next_asset";
                    var targetId = new AssetId(choice.NextAsset!);
                    var missingAsset = AssetPackValidation.MissingReference(pack, targetId, entry.SourcePath, nextAssetPath);
                    if (missingAsset is not null)
                    {
                        diagnostics.Add(missingAsset with
                        {
                            Code = "dialogue.missing_choice_asset",
                            Message = $"Choice '{choice.Id}' on node '{nodeId}' references missing dialogue asset '{targetId}'.",
                            Span = entry.SourceMap is not null && entry.SourceMap.TryGetSpan(nextAssetPath, out var span) ? span : null
                        });
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(choice.NextNode))
                    {
                        continue;
                    }

                    var target = pack.Assets[targetId].Asset;
                    if (!target.Nodes.ContainsKey(choice.NextNode!))
                    {
                        var nextNodePath = $"nodes.{nodeId}.choices[{choiceIndex}].next_node";
                        diagnostics.Add(AssetValidation.Error(
                            "dialogue.missing_choice_asset_node",
                            $"Choice '{choice.Id}' on node '{nodeId}' references missing node '{choice.NextNode}' in dialogue asset '{targetId}'.",
                            entry.SourcePath,
                            keyPath: nextNodePath,
                            span: entry.SourceMap is not null && entry.SourceMap.TryGetSpan(nextNodePath, out var span) ? span : null));
                    }
                }
            }
        }

        return diagnostics;
    }
}
