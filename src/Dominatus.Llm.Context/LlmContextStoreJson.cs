using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dominatus.Llm.Context;

public static class LlmContextStoreJson
{
    public const string Format = "dominatus.llm.context.store";
    public const int FormatVersion = 1;

    public static string Serialize(LlmContextStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var dto = new LlmContextStoreDto
        {
            Format = Format,
            Version = FormatVersion,
            Id = store.Id,
            Title = store.Title,
            CreatedUtc = store.CreatedUtc,
            UpdatedUtc = store.UpdatedUtc,
            Chunks = store.Chunks,
            Loadouts = store.Loadouts
        };

        return JsonSerializer.Serialize(dto, LlmContextJsonContext.Default.LlmContextStoreDto);
    }

    public static LlmContextStore Deserialize(string json)
    {
        LlmContextStoreDto dto;
        try
        {
            dto = JsonSerializer.Deserialize(json, LlmContextJsonContext.Default.LlmContextStoreDto)
                ?? throw new InvalidOperationException("Context store json could not be parsed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Invalid context store json.", ex);
        }
        if (!string.Equals(dto.Format, Format, StringComparison.Ordinal)) throw new InvalidOperationException($"Unsupported format: {dto.Format}");
        if (dto.Version != FormatVersion) throw new InvalidOperationException($"Unsupported format version: {dto.Version}");

        var store = new LlmContextStore(dto.Id, dto.Title, dto.CreatedUtc);
        foreach (var chunk in dto.Chunks)
        {
            store.Upsert(chunk);
        }
        foreach (var loadout in dto.Loadouts ?? [])
        {
            store.UpsertLoadout(loadout);
        }

        return store;
    }

    public static void Save(string path, LlmContextStore store)
        => File.WriteAllText(path, Serialize(store));

    public static LlmContextStore Load(string path)
        => Deserialize(File.ReadAllText(path));
}

public sealed record LlmContextStoreDto
{
    public required string Format { get; init; }
    public required int Version { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public IReadOnlyList<LlmContextChunk> Chunks { get; init; } = [];
    public IReadOnlyList<LlmContextLoadout> Loadouts { get; init; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LlmContextStoreDto))]
internal partial class LlmContextJsonContext : JsonSerializerContext;
