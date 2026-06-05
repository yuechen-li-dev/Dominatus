using Dominatus.Assets.Toml;

namespace Dominatus.Assets.Toml.AriadneDialogue;

public sealed class DialogueAssetValidator : IAssetValidator<DialogueAsset>
{
    public IReadOnlyList<AssetDiagnostic> Validate(DialogueAsset asset, AssetValidationContext context)
    {
        var diagnostics = new List<AssetDiagnostic>();

        if (string.IsNullOrWhiteSpace(asset.Id))
        {
            diagnostics.Add(AssetValidation.Required("id", context.SourcePath));
        }

        if (string.IsNullOrWhiteSpace(asset.Start))
        {
            diagnostics.Add(AssetValidation.Required("start", context.SourcePath));
        }

        if (asset.Nodes is null || asset.Nodes.Count == 0)
        {
            diagnostics.Add(AssetValidation.Error("DIALOGUE_NODES_EMPTY", "Dialogue must contain at least one node.", context.SourcePath));
            return diagnostics;
        }

        if (!string.IsNullOrWhiteSpace(asset.Start) && !asset.Nodes.ContainsKey(asset.Start))
        {
            diagnostics.Add(AssetValidation.Error("DIALOGUE_START_MISSING", $"Start node '{asset.Start}' does not exist.", context.SourcePath));
        }

        foreach (var (nodeId, node) in asset.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Speaker))
            {
                diagnostics.Add(AssetValidation.Required($"nodes.{nodeId}.speaker", context.SourcePath));
            }

            if (string.IsNullOrWhiteSpace(node.Text))
            {
                diagnostics.Add(AssetValidation.Required($"nodes.{nodeId}.text", context.SourcePath));
            }

            var choiceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var choice in node.Choices)
            {
                ValidateChoice(asset, nodeId, choice, choiceIds, context.SourcePath, diagnostics);
            }
        }

        return diagnostics;
    }

    private static void ValidateChoice(
        DialogueAsset asset,
        string nodeId,
        DialogueChoiceAsset choice,
        HashSet<string> choiceIds,
        string? sourcePath,
        List<AssetDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(choice.Id))
        {
            diagnostics.Add(AssetValidation.Required($"nodes.{nodeId}.choices[].id", sourcePath));
        }
        else if (!choiceIds.Add(choice.Id))
        {
            diagnostics.Add(AssetValidation.Error("DIALOGUE_CHOICE_ID_DUPLICATE", $"Node '{nodeId}' contains duplicate choice id '{choice.Id}'.", sourcePath));
        }

        if (string.IsNullOrWhiteSpace(choice.Text))
        {
            diagnostics.Add(AssetValidation.Required($"nodes.{nodeId}.choices[{choice.Id}].text", sourcePath));
        }

        var hasLocalNext = !string.IsNullOrWhiteSpace(choice.Next);
        var hasNextAsset = !string.IsNullOrWhiteSpace(choice.NextAsset);
        var hasNextNode = !string.IsNullOrWhiteSpace(choice.NextNode);

        if (hasLocalNext && (hasNextAsset || hasNextNode))
        {
            diagnostics.Add(AssetValidation.Error("DIALOGUE_CHOICE_TARGET_AMBIGUOUS", $"Choice '{choice.Id}' on node '{nodeId}' cannot set both next and next_asset/next_node.", sourcePath));
        }

        if (hasLocalNext)
        {
            if (!asset.Nodes.ContainsKey(choice.Next!))
            {
                diagnostics.Add(AssetValidation.Error("DIALOGUE_CHOICE_TARGET_MISSING", $"Choice '{choice.Id}' on node '{nodeId}' points to missing node '{choice.Next}'.", sourcePath));
            }

            return;
        }

        if (hasNextAsset)
        {
            if (!hasNextNode)
            {
                diagnostics.Add(AssetValidation.Required($"nodes.{nodeId}.choices[{choice.Id}].next_node", sourcePath));
            }

            return;
        }

        diagnostics.Add(AssetValidation.Required($"nodes.{nodeId}.choices[{choice.Id}].next", sourcePath));
    }
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
                foreach (var choice in node.Choices.Where(choice => !string.IsNullOrWhiteSpace(choice.NextAsset)))
                {
                    var targetId = new AssetId(choice.NextAsset!);
                    var missingAsset = AssetPackValidation.MissingReference(pack, targetId, entry.SourcePath, $"nodes.{nodeId}.choices[{choice.Id}].next_asset");
                    if (missingAsset is not null)
                    {
                        diagnostics.Add(missingAsset with
                        {
                            Code = "DIALOGUE_CHOICE_ASSET_MISSING",
                            Message = $"Choice '{choice.Id}' on node '{nodeId}' references missing dialogue asset '{targetId}'."
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
                        diagnostics.Add(AssetValidation.Error(
                            "DIALOGUE_CHOICE_ASSET_NODE_MISSING",
                            $"Choice '{choice.Id}' on node '{nodeId}' references missing node '{choice.NextNode}' in dialogue asset '{targetId}'.",
                            entry.SourcePath));
                    }
                }
            }
        }

        return diagnostics;
    }
}
