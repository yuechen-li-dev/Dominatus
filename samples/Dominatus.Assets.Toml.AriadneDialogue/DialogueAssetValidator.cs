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
                if (string.IsNullOrWhiteSpace(choice.Id))
                {
                    diagnostics.Add(AssetValidation.Required($"nodes.{nodeId}.choices[].id", context.SourcePath));
                }
                else if (!choiceIds.Add(choice.Id))
                {
                    diagnostics.Add(AssetValidation.Error("DIALOGUE_CHOICE_ID_DUPLICATE", $"Node '{nodeId}' contains duplicate choice id '{choice.Id}'.", context.SourcePath));
                }

                if (string.IsNullOrWhiteSpace(choice.Text))
                {
                    diagnostics.Add(AssetValidation.Required($"nodes.{nodeId}.choices[{choice.Id}].text", context.SourcePath));
                }

                if (string.IsNullOrWhiteSpace(choice.Next))
                {
                    diagnostics.Add(AssetValidation.Required($"nodes.{nodeId}.choices[{choice.Id}].next", context.SourcePath));
                }
                else if (!asset.Nodes.ContainsKey(choice.Next))
                {
                    diagnostics.Add(AssetValidation.Error("DIALOGUE_CHOICE_TARGET_MISSING", $"Choice '{choice.Id}' on node '{nodeId}' points to missing node '{choice.Next}'.", context.SourcePath));
                }
            }
        }

        return diagnostics;
    }
}
