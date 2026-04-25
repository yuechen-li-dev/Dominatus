using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

public static class LlmDecisionRequestHasher
{
    public static string ComputeHash(LlmDecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var canonicalRequest = BuildCanonicalRequestJson(request);
        var utf8 = Encoding.UTF8.GetBytes(canonicalRequest);
        var hash = SHA256.HashData(utf8);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildCanonicalRequestJson(LlmDecisionRequest request)
    {
        var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        writer.WriteString("canonicalContextJson", request.CanonicalContextJson);
        writer.WriteString("intent", request.Intent);
        writer.WriteString("outputContractVersion", request.OutputContractVersion);
        writer.WriteString("persona", request.Persona);
        writer.WriteString("promptTemplateVersion", request.PromptTemplateVersion);

        writer.WritePropertyName("options");
        writer.WriteStartArray();
        foreach (var option in request.Options.OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", option.Id);
            writer.WriteString("description", option.Description);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WritePropertyName("sampling");
        writer.WriteStartObject();
        writer.WriteNumber("temperature", request.Sampling.Temperature);
        if (request.Sampling.TopP is not null)
        {
            writer.WriteNumber("topP", request.Sampling.TopP.Value);
        }

        if (request.Sampling.MaxOutputTokens is not null)
        {
            writer.WriteNumber("maxOutputTokens", request.Sampling.MaxOutputTokens.Value);
        }

        writer.WriteString("model", request.Sampling.Model);
        writer.WriteString("provider", request.Sampling.Provider);
        writer.WriteEndObject();

        writer.WriteString("stableId", request.StableId);
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
