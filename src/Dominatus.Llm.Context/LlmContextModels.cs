using System.Text;

namespace Dominatus.Llm.Context;

public sealed record LlmContextChunk
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public int Version { get; init; } = 1;
    public int Priority { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? Source { get; init; }
    public string? Summary { get; init; }
}

public sealed record LlmContextQuery
{
    public IReadOnlyList<string> IncludeKinds { get; init; } = [];
    public IReadOnlyList<string> RequiredChunkIds { get; init; } = [];
    public IReadOnlyList<string> IncludeTags { get; init; } = [];
    public IReadOnlyList<string> ExcludeTags { get; init; } = [];
    public int MaxChars { get; init; } = 16_000;
    public bool IncludeExpired { get; init; }
}

public sealed record LlmContextPacket(
    string StoreId,
    string QuerySummary,
    string Text,
    IReadOnlyList<string> IncludedChunkIds,
    IReadOnlyList<string> OmittedChunkIds,
    int CharacterCount);

public sealed class LlmContextStore
{
    private readonly Dictionary<string, LlmContextChunk> _chunks = new(StringComparer.Ordinal);

    public LlmContextStore(string id, string title, DateTimeOffset createdUtc)
    {
        Id = RequireText(id, nameof(id));
        Title = RequireText(title, nameof(title));
        CreatedUtc = createdUtc;
        UpdatedUtc = createdUtc;
    }

    public string Id { get; }
    public string Title { get; }
    public int Version { get; private set; } = 1;
    public DateTimeOffset CreatedUtc { get; }
    public DateTimeOffset UpdatedUtc { get; private set; }
    public IReadOnlyList<LlmContextChunk> Chunks => _chunks.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();

    public void Upsert(LlmContextChunk chunk)
    {
        var validated = ValidateChunk(chunk);
        _chunks[validated.Id] = validated;
        Version++;
        UpdatedUtc = validated.UpdatedUtc;
    }

    public bool Remove(string id)
    {
        if (_chunks.Remove(RequireText(id, nameof(id))))
        {
            Version++;
            return true;
        }

        return false;
    }

    public LlmContextChunk? Find(string id)
        => _chunks.TryGetValue(RequireText(id, nameof(id)), out var chunk) ? chunk : null;

    public IReadOnlyList<LlmContextChunk> Select(LlmContextQuery query, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateQuery(query);

        var requiredMap = query.RequiredChunkIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
        var includeKinds = query.IncludeKinds.Count == 0 ? null : new HashSet<string>(query.IncludeKinds, StringComparer.Ordinal);
        var includeTags = query.IncludeTags.Count == 0 ? null : new HashSet<string>(query.IncludeTags, StringComparer.Ordinal);
        var excludeTags = query.ExcludeTags.Count == 0 ? null : new HashSet<string>(query.ExcludeTags, StringComparer.Ordinal);

        var selected = _chunks.Values.Where(c =>
        {
            var isRequired = requiredMap.ContainsKey(c.Id);
            var expired = c.ExpiresAtUtc is not null && c.ExpiresAtUtc <= nowUtc;
            if (!query.IncludeExpired && expired)
            {
                return false;
            }

            if (excludeTags is not null && c.Tags.Any(excludeTags.Contains))
            {
                return false;
            }

            if (isRequired)
            {
                return true;
            }

            if (includeKinds is not null && !includeKinds.Contains(c.Kind))
            {
                return false;
            }

            return includeTags is null || c.Tags.Any(includeTags.Contains);
        });

        return selected
            .OrderBy(c => requiredMap.TryGetValue(c.Id, out var idx) ? idx : int.MaxValue)
            .ThenByDescending(c => c.Priority)
            .ThenByDescending(c => c.UpdatedUtc)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public LlmContextPacket BuildPacket(LlmContextQuery query, DateTimeOffset nowUtc)
    {
        var selected = Select(query, nowUtc);
        var included = new List<string>();
        var omitted = new List<string>();
        var sb = new StringBuilder();
        sb.AppendLine("# Dominatus LLM Context Packet");
        sb.AppendLine($"Store: {Title} ({Id})");
        sb.AppendLine($"GeneratedUtc: {nowUtc:O}");
        sb.AppendLine($"MaxChars: {query.MaxChars}");
        sb.AppendLine();

        var requiredSet = new HashSet<string>(query.RequiredChunkIds, StringComparer.Ordinal);
        foreach (var chunk in selected)
        {
            var block = RenderChunk(chunk);
            if (sb.Length + block.Length > query.MaxChars)
            {
                if (requiredSet.Contains(chunk.Id))
                {
                    throw new InvalidOperationException($"Required chunk '{chunk.Id}' exceeds MaxChars budget.");
                }

                omitted.Add(chunk.Id);
                continue;
            }

            sb.Append(block);
            included.Add(chunk.Id);
        }

        return new LlmContextPacket(Id, $"kinds={string.Join(',', query.IncludeKinds)};maxChars={query.MaxChars}", sb.ToString(), included, omitted, sb.Length);
    }

    private static string RenderChunk(LlmContextChunk chunk)
    {
        var tags = chunk.Tags.Count == 0 ? "(none)" : string.Join(", ", chunk.Tags);
        var src = string.IsNullOrWhiteSpace(chunk.Source) ? "(none)" : chunk.Source;
        return $"## {chunk.Kind}: {chunk.Title}\nId: {chunk.Id}\nVersion: {chunk.Version}\nUpdatedUtc: {chunk.UpdatedUtc:O}\nTags: {tags}\nSource: {src}\n\n{chunk.Content}\n\n";
    }

    private static void ValidateQuery(LlmContextQuery query)
    {
        if (query.MaxChars <= 0) throw new ArgumentOutOfRangeException(nameof(query.MaxChars));
    }

    private static LlmContextChunk ValidateChunk(LlmContextChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (chunk.Version <= 0) throw new ArgumentOutOfRangeException(nameof(chunk.Version));

        var tags = chunk.Tags ?? throw new ArgumentException("Tags cannot be null.", nameof(chunk));
        if (tags.Any(x => string.IsNullOrWhiteSpace(x))) throw new ArgumentException("Tags cannot contain empty entries.", nameof(chunk));

        var created = chunk.CreatedUtc == default ? DateTimeOffset.UtcNow : chunk.CreatedUtc;
        var updated = chunk.UpdatedUtc == default ? created : chunk.UpdatedUtc;

        return chunk with
        {
            Id = RequireText(chunk.Id, nameof(chunk.Id)),
            Kind = RequireText(chunk.Kind, nameof(chunk.Kind)),
            Title = RequireText(chunk.Title, nameof(chunk.Title)),
            Content = RequireText(chunk.Content, nameof(chunk.Content)),
            CreatedUtc = created,
            UpdatedUtc = updated,
            Tags = tags.ToArray()
        };
    }

    private static string RequireText(string value, string param)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", param);
        return value;
    }
}
