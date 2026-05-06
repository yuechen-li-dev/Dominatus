using System.Text;

namespace Dominatus.Llm.OptFlow;

internal static class LlmDecisionPromptBuilder
{
    public static string BuildPrompt(LlmDecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sb = new StringBuilder();
        sb.AppendLine("You are scoring a closed set of runtime-provided options.");
        sb.AppendLine($"stableId: {request.StableId}");
        sb.AppendLine($"intent: {request.Intent}");
        sb.AppendLine($"persona: {request.Persona}");
        sb.AppendLine($"promptTemplateVersion: {request.PromptTemplateVersion}");
        sb.AppendLine($"outputContractVersion: {request.OutputContractVersion}");
        sb.AppendLine();
        sb.AppendLine("canonicalContextJson:");
        sb.AppendLine(request.CanonicalContextJson);
        sb.AppendLine();
        sb.AppendLine("options (sorted by id):");

        foreach (var option in request.Options.OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            sb.AppendLine($"- id: {option.Id}");
            sb.AppendLine($"  description: {option.Description}");
        }

        sb.AppendLine();
        sb.AppendLine("Requirements:");
        sb.AppendLine("- Score every option exactly once with score in [0.0, 1.0].");
        sb.AppendLine("- Rank every option exactly once; ranks must be 1..N.");
        sb.AppendLine($"- Each option rationale must be short (<= {LlmDecisionOptionScore.MaxRationaleLength} chars).");
        sb.AppendLine($"- Overall rationale must be short (<= {LlmDecisionResult.MaxRationaleLength} chars).");
        sb.AppendLine("- You must score every authored option.");
        sb.AppendLine("- If at least one option is acceptable, set outcome = \"chosen\".");
        sb.AppendLine("- If all options are unacceptable, unsafe, invalid, impossible, or wrongly framed, set outcome = \"refused\".");
        sb.AppendLine("- Refusal is a valid decision outcome.");
        sb.AppendLine("- When outcome = \"refused\", refusal.reason is required.");
        sb.AppendLine("- Do not invent a new executable option.");
        sb.AppendLine("- proposedAlternative is non-executable text only.");
        sb.AppendLine("- If proposed alternatives are not allowed, omit refusal.proposedAlternative or set it null.");
        sb.AppendLine("- Return strict JSON only, no markdown, no extra keys.");
        sb.AppendLine("- JSON schema:");
        sb.AppendLine("{");
        sb.AppendLine("  \"requestHash\": \"...\",");
        sb.AppendLine("  \"outcome\": \"chosen|refused\",");
        sb.AppendLine("  \"scores\": [");
        sb.AppendLine("    { \"optionId\": \"option-id\", \"score\": 0.0, \"rank\": 1, \"rationale\": \"short reason\" }");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"rationale\": \"short overall reason\",");
        sb.AppendLine("  \"refusal\": { \"reason\": \"...\", \"proposedAlternative\": \"...\" }");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
