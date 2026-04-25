using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

public static class LlmMagiRequestHasher
{
    public static string ComputeHash(LlmMagiRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var canonical = BuildCanonicalRequestJson(request);
        var utf8 = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(utf8);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildCanonicalRequestJson(LlmMagiRequest request)
    {
        var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        writer.WriteString("stableId", request.StableId);
        writer.WriteString("intent", request.Intent);
        writer.WriteString("persona", request.Persona);
        writer.WriteString("canonicalContextJson", request.CanonicalContextJson);

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

        WriteParticipant(writer, "advocateA", request.AdvocateA);
        WriteParticipant(writer, "advocateB", request.AdvocateB);
        WriteParticipant(writer, "judge", request.Judge);

        writer.WriteString("promptTemplateVersion", request.PromptTemplateVersion);
        writer.WriteString("outputContractVersion", request.OutputContractVersion);

        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteParticipant(Utf8JsonWriter writer, string roleKey, LlmMagiParticipant participant)
    {
        writer.WritePropertyName(roleKey);
        writer.WriteStartObject();
        writer.WriteString("id", participant.Id);
        writer.WriteString("stance", participant.Stance);

        writer.WritePropertyName("sampling");
        writer.WriteStartObject();
        writer.WriteString("provider", participant.Sampling.Provider);
        writer.WriteString("model", participant.Sampling.Model);
        writer.WriteNumber("temperature", participant.Sampling.Temperature);

        if (participant.Sampling.MaxOutputTokens is int maxTokens)
        {
            writer.WriteNumber("maxOutputTokens", maxTokens);
        }

        if (participant.Sampling.TopP is double topP)
        {
            writer.WriteNumber("topP", topP);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
