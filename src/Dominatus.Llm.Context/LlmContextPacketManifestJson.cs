using System.Text.Json;

namespace Dominatus.Llm.Context;

public static class LlmContextPacketManifestJson
{
    public static string Serialize(LlmContextPacketManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.Serialize(manifest, LlmContextJsonContext.Default.LlmContextPacketManifest);
    }

    public static LlmContextPacketManifest Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, LlmContextJsonContext.Default.LlmContextPacketManifest)
                ?? throw new InvalidOperationException("Packet manifest json could not be parsed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Invalid packet manifest json.", ex);
        }
    }
}
