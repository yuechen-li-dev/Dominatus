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

public sealed record LlmContextLoadout
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> IncludeKinds { get; init; } = [];
    public IReadOnlyList<string> RequiredChunkIds { get; init; } = [];
    public IReadOnlyList<string> IncludeTags { get; init; } = [];
    public IReadOnlyList<string> ExcludeTags { get; init; } = [];
    public int MaxChars { get; init; } = 16_000;
    public bool IncludeExpired { get; init; }

    public LlmContextQuery ToQuery()
        => new()
        {
            IncludeKinds = IncludeKinds.ToArray(),
            RequiredChunkIds = RequiredChunkIds.ToArray(),
            IncludeTags = IncludeTags.ToArray(),
            ExcludeTags = ExcludeTags.ToArray(),
            MaxChars = MaxChars,
            IncludeExpired = IncludeExpired
        };
}

public enum LlmContextPacketSourceKind
{
    Query,
    Loadout
}

public sealed record LlmContextPacketProvenance
{
    public LlmContextPacketSourceKind SourceKind { get; init; } = LlmContextPacketSourceKind.Query;
    public string? LoadoutId { get; init; }
    public string? LoadoutTitle { get; init; }
    public string? LoadoutDescription { get; init; }
    public IReadOnlyList<string> IncludeKinds { get; init; } = [];
    public IReadOnlyList<string> RequiredChunkIds { get; init; } = [];
    public IReadOnlyList<string> IncludeTags { get; init; } = [];
    public IReadOnlyList<string> ExcludeTags { get; init; } = [];
    public int MaxChars { get; init; }
    public bool IncludeExpired { get; init; }
}

public sealed record LlmContextPacket(
    string StoreId,
    string QuerySummary,
    string Text,
    IReadOnlyList<string> IncludedChunkIds,
    IReadOnlyList<string> OmittedChunkIds,
    int CharacterCount)
{
    public IReadOnlyList<LlmContextPacketChunkDiagnostic> Diagnostics { get; init; } = [];
    public int MaxChars { get; init; }
    public int RemainingChars { get; init; }
    public bool WasBudgetConstrained { get; init; }
    public LlmContextPacketProvenance Provenance { get; init; } = new();

    public LlmContextPacketManifest ToManifest()
        => new()
        {
            StoreId = StoreId,
            QuerySummary = QuerySummary,
            MaxChars = MaxChars,
            CharacterCount = CharacterCount,
            RemainingChars = RemainingChars,
            WasBudgetConstrained = WasBudgetConstrained,
            IncludedChunkIds = IncludedChunkIds.ToArray(),
            OmittedChunkIds = OmittedChunkIds.ToArray(),
            Diagnostics = Diagnostics.ToArray(),
            LoadoutId = Provenance.LoadoutId,
            Provenance = Provenance with { }
        };
}

public enum LlmContextPacketChunkStatus { Included, Omitted }
public enum LlmContextPacketOmissionReason { None, Expired, KindFilter, IncludeTagFilter, ExcludeTagFilter, BudgetExceeded, RequiredOverflow, Duplicate, Unknown }

public sealed record LlmContextPacketChunkDiagnostic
{
    public required string ChunkId { get; init; }
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public LlmContextPacketChunkStatus Status { get; init; }
    public string StatusName { get; init; } = nameof(LlmContextPacketChunkStatus.Included);
    public LlmContextPacketOmissionReason OmissionReason { get; init; } = LlmContextPacketOmissionReason.None;
    public string OmissionReasonName { get; init; } = nameof(LlmContextPacketOmissionReason.None);
    public bool IsRequired { get; init; }
    public int Priority { get; init; }
    public int CharacterCount { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed record LlmContextPacketManifest
{
    public required string StoreId { get; init; }
    public string? LoadoutId { get; init; }
    public required string QuerySummary { get; init; }
    public int MaxChars { get; init; }
    public int CharacterCount { get; init; }
    public int RemainingChars { get; init; }
    public bool WasBudgetConstrained { get; init; }
    public IReadOnlyList<string> IncludedChunkIds { get; init; } = [];
    public IReadOnlyList<string> OmittedChunkIds { get; init; } = [];
    public IReadOnlyList<LlmContextPacketChunkDiagnostic> Diagnostics { get; init; } = [];
    public LlmContextPacketProvenance Provenance { get; init; } = new();
}

public sealed class LlmContextStore
{
    private readonly Dictionary<string, LlmContextChunk> _chunks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LlmContextLoadout> _loadouts = new(StringComparer.Ordinal);

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
    public IReadOnlyList<LlmContextLoadout> Loadouts => _loadouts.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();

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

    public void UpsertLoadout(LlmContextLoadout loadout)
    {
        var validated = ValidateLoadout(loadout);
        _loadouts[validated.Id] = validated;
        Version++;
        UpdatedUtc = DateTimeOffset.UtcNow;
    }

    public bool RemoveLoadout(string id)
    {
        if (_loadouts.Remove(RequireText(id, nameof(id))))
        {
            Version++;
            return true;
        }

        return false;
    }

    public LlmContextLoadout? FindLoadout(string id)
        => _loadouts.TryGetValue(RequireText(id, nameof(id)), out var loadout) ? loadout : null;

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
        ValidateQuery(query);
        var included = new List<string>();
        var omitted = new List<string>();
        var diagnostics = new List<LlmContextPacketChunkDiagnostic>();
        var sb = new StringBuilder();
        sb.AppendLine("# Dominatus LLM Context Packet");
        sb.AppendLine($"Store: {Title} ({Id})");
        sb.AppendLine($"GeneratedUtc: {nowUtc:O}");
        sb.AppendLine($"MaxChars: {query.MaxChars}");
        sb.AppendLine();

        var requiredSet = new HashSet<string>(query.RequiredChunkIds, StringComparer.Ordinal);
        var includeKinds = query.IncludeKinds.Count == 0 ? null : new HashSet<string>(query.IncludeKinds, StringComparer.Ordinal);
        var includeTags = query.IncludeTags.Count == 0 ? null : new HashSet<string>(query.IncludeTags, StringComparer.Ordinal);
        var excludeTags = query.ExcludeTags.Count == 0 ? null : new HashSet<string>(query.ExcludeTags, StringComparer.Ordinal);
        var requiredMap = query.RequiredChunkIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);

        foreach (var chunk in _chunks.Values.OrderBy(c => requiredMap.TryGetValue(c.Id, out var idx) ? idx : int.MaxValue).ThenByDescending(c => c.Priority).ThenByDescending(c => c.UpdatedUtc).ThenBy(c => c.Id, StringComparer.Ordinal))
        {
            var isRequired = requiredSet.Contains(chunk.Id);
            var block = RenderChunk(chunk);
            LlmContextPacketOmissionReason reason = LlmContextPacketOmissionReason.None;
            if (!query.IncludeExpired && chunk.ExpiresAtUtc is not null && chunk.ExpiresAtUtc <= nowUtc) reason = LlmContextPacketOmissionReason.Expired;
            else if (excludeTags is not null && chunk.Tags.Any(excludeTags.Contains)) reason = LlmContextPacketOmissionReason.ExcludeTagFilter;
            else if (!isRequired && includeKinds is not null && !includeKinds.Contains(chunk.Kind)) reason = LlmContextPacketOmissionReason.KindFilter;
            else if (!isRequired && includeTags is not null && !chunk.Tags.Any(includeTags.Contains)) reason = LlmContextPacketOmissionReason.IncludeTagFilter;

            if (reason != LlmContextPacketOmissionReason.None)
            {
                omitted.Add(chunk.Id);
                diagnostics.Add(NewDiagnostic(chunk, LlmContextPacketChunkStatus.Omitted, reason, isRequired, block.Length));
                continue;
            }

            if (sb.Length + block.Length > query.MaxChars)
            {
                if (isRequired)
                {
                    throw new InvalidOperationException($"Required chunk '{chunk.Id}' exceeds MaxChars budget.");
                }

                omitted.Add(chunk.Id);
                diagnostics.Add(NewDiagnostic(chunk, LlmContextPacketChunkStatus.Omitted, LlmContextPacketOmissionReason.BudgetExceeded, isRequired, block.Length));
                continue;
            }

            sb.Append(block);
            included.Add(chunk.Id);
            diagnostics.Add(NewDiagnostic(chunk, LlmContextPacketChunkStatus.Included, LlmContextPacketOmissionReason.None, isRequired, block.Length));
        }

        return new LlmContextPacket(Id, $"kinds={string.Join(',', query.IncludeKinds)};maxChars={query.MaxChars}", sb.ToString(), included, omitted, sb.Length)
        {
            Diagnostics = diagnostics,
            MaxChars = query.MaxChars,
            RemainingChars = Math.Max(0, query.MaxChars - sb.Length),
            WasBudgetConstrained = omitted.Count > 0 && diagnostics.Any(d => d.OmissionReason == LlmContextPacketOmissionReason.BudgetExceeded),
            Provenance = QueryProvenance(query)
        };
    }

    public LlmContextPacket BuildPacket(string loadoutId, DateTimeOffset nowUtc)
    {
        var loadout = FindLoadout(loadoutId)
            ?? throw new InvalidOperationException($"Loadout '{loadoutId}' was not found.");
        var query = loadout.ToQuery();
        var packet = BuildPacket(query, nowUtc);
        return packet with
        {
            QuerySummary = $"loadout={loadout.Id};title={loadout.Title};maxChars={loadout.MaxChars}",
            Provenance = LoadoutProvenance(loadout, query)
        };
    }

    private static LlmContextPacketProvenance QueryProvenance(LlmContextQuery query)
        => new()
        {
            SourceKind = LlmContextPacketSourceKind.Query,
            IncludeKinds = query.IncludeKinds.ToArray(),
            RequiredChunkIds = query.RequiredChunkIds.ToArray(),
            IncludeTags = query.IncludeTags.ToArray(),
            ExcludeTags = query.ExcludeTags.ToArray(),
            MaxChars = query.MaxChars,
            IncludeExpired = query.IncludeExpired
        };

    private static LlmContextPacketProvenance LoadoutProvenance(LlmContextLoadout loadout, LlmContextQuery query)
        => QueryProvenance(query) with
        {
            SourceKind = LlmContextPacketSourceKind.Loadout,
            LoadoutId = loadout.Id,
            LoadoutTitle = loadout.Title,
            LoadoutDescription = loadout.Description
        };

    private static string RenderChunk(LlmContextChunk chunk)
    {
        var tags = chunk.Tags.Count == 0 ? "(none)" : string.Join(", ", chunk.Tags);
        var src = string.IsNullOrWhiteSpace(chunk.Source) ? "(none)" : chunk.Source;
        return $"## {chunk.Kind}: {chunk.Title}\nId: {chunk.Id}\nVersion: {chunk.Version}\nUpdatedUtc: {chunk.UpdatedUtc:O}\nTags: {tags}\nSource: {src}\n\n{chunk.Content}\n\n";
    }

    private static LlmContextPacketChunkDiagnostic NewDiagnostic(LlmContextChunk chunk, LlmContextPacketChunkStatus status, LlmContextPacketOmissionReason reason, bool isRequired, int characterCount)
        => new()
        {
            ChunkId = chunk.Id,
            Kind = chunk.Kind,
            Title = chunk.Title,
            Status = status,
            OmissionReason = reason,
            StatusName = status.ToString(),
            OmissionReasonName = reason.ToString(),
            IsRequired = isRequired,
            Priority = chunk.Priority,
            CharacterCount = characterCount,
            ExpiresAtUtc = chunk.ExpiresAtUtc,
            Tags = chunk.Tags.ToArray()
        };

    private static void ValidateQuery(LlmContextQuery query)
    {
        if (query.MaxChars <= 0) throw new ArgumentOutOfRangeException(nameof(query.MaxChars));
    }

    private static LlmContextLoadout ValidateLoadout(LlmContextLoadout loadout)
    {
        ArgumentNullException.ThrowIfNull(loadout);
        if (loadout.MaxChars <= 0) throw new ArgumentOutOfRangeException(nameof(loadout.MaxChars));

        var includeKinds = ValidateUniqueTextList(loadout.IncludeKinds, nameof(loadout.IncludeKinds));
        var requiredChunkIds = ValidateUniqueTextList(loadout.RequiredChunkIds, nameof(loadout.RequiredChunkIds));
        var includeTags = ValidateUniqueTextList(loadout.IncludeTags, nameof(loadout.IncludeTags));
        var excludeTags = ValidateUniqueTextList(loadout.ExcludeTags, nameof(loadout.ExcludeTags));

        return loadout with
        {
            Id = RequireText(loadout.Id, nameof(loadout.Id)),
            Title = RequireText(loadout.Title, nameof(loadout.Title)),
            IncludeKinds = includeKinds,
            RequiredChunkIds = requiredChunkIds,
            IncludeTags = includeTags,
            ExcludeTags = excludeTags
        };
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

    private static IReadOnlyList<string> ValidateUniqueTextList(IReadOnlyList<string> items, string paramName)
    {
        ArgumentNullException.ThrowIfNull(items, paramName);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>(items.Count);
        foreach (var item in items)
        {
            var value = RequireText(item, paramName);
            if (!seen.Add(value))
            {
                throw new ArgumentException($"Duplicate entry '{value}'.", paramName);
            }

            normalized.Add(value);
        }

        return normalized;
    }
}
