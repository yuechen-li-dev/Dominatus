using System.Text;
using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

public sealed class JsonLlmCassette : ILlmCassette
{
    private const string SchemaVersion = "dom.llm.cassette.v1";

    private readonly string _path;
    private readonly Dictionary<string, CassetteEntry> _entries;

    public JsonLlmCassette(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _entries = new Dictionary<string, CassetteEntry>(StringComparer.Ordinal);
    }

    private JsonLlmCassette(string path, Dictionary<string, CassetteEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _entries = entries;
    }

    public static JsonLlmCassette LoadOrCreate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return new JsonLlmCassette(path);
        }

        var jsonText = File.ReadAllText(path, Encoding.UTF8);

        CassetteFileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<CassetteFileDto>(jsonText, CreateSerializerOptions(writeIndented: false));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Cassette JSON at '{path}' is malformed. {ex.Message}", ex);
        }

        if (dto is null)
        {
            throw new InvalidOperationException($"Cassette JSON at '{path}' is empty or invalid.");
        }

        if (!string.Equals(dto.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unsupported cassette schemaVersion '{dto.SchemaVersion ?? "<null>"}' at '{path}'. Expected '{SchemaVersion}'.");
        }

        if (dto.Entries is null)
        {
            throw new InvalidOperationException($"Cassette JSON at '{path}' is missing required property 'entries'.");
        }

        var entries = new Dictionary<string, CassetteEntry>(StringComparer.Ordinal);

        for (int i = 0; i < dto.Entries.Count; i++)
        {
            var entryDto = dto.Entries[i] ?? throw new InvalidOperationException($"Cassette entry at index {i} is null.");
            var entry = ParseAndValidateEntry(entryDto, $"entries[{i}]");

            if (!entries.TryAdd(entry.RequestHash, entry))
            {
                throw new InvalidOperationException($"Cassette contains duplicate requestHash '{entry.RequestHash}' at entries[{i}].");
            }
        }

        return new JsonLlmCassette(path, entries);
    }

    public bool TryGet(string requestHash, out LlmTextResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);

        if (_entries.TryGetValue(requestHash, out var entry))
        {
            result = entry.Result;
            return true;
        }

        result = default!;
        return false;
    }

    public void Put(string requestHash, LlmTextRequest request, LlmTextResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        var validated = ValidateEntry(new CassetteEntry(requestHash, request, result), "put");

        if (_entries.TryGetValue(requestHash, out var existing))
        {
            if (string.Equals(existing.Result.Text, validated.Result.Text, StringComparison.Ordinal))
            {
                if (existing == validated)
                {
                    return;
                }

                throw new InvalidOperationException($"Duplicate hash '{requestHash}' has matching text but mismatched metadata.");
            }

            throw new InvalidOperationException($"Duplicate hash '{requestHash}' has a different text payload.");
        }

        _entries[requestHash] = validated;
    }

    public void Save()
    {
        var parent = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var dto = new CassetteFileDto
        {
            SchemaVersion = SchemaVersion,
            Entries = _entries
                .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => ToDto(kvp.Value))
                .ToList()
        };

        var json = JsonSerializer.Serialize(dto, CreateSerializerOptions(writeIndented: true));
        var tempPath = _path + ".tmp";

        File.WriteAllText(tempPath, json, Encoding.UTF8);
        File.Move(tempPath, _path, overwrite: true);
    }


    private static JsonSerializerOptions CreateSerializerOptions(bool writeIndented)
        => new()
        {
            WriteIndented = writeIndented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

    private static CassetteEntry ParseAndValidateEntry(CassetteEntryDto dto, string path)
    {
        if (string.IsNullOrWhiteSpace(dto.RequestHash))
        {
            throw new InvalidOperationException($"Cassette {path}.requestHash must be non-empty.");
        }

        var requestDto = dto.Request ?? throw new InvalidOperationException($"Cassette {path}.request is required.");
        var resultDto = dto.Result ?? throw new InvalidOperationException($"Cassette {path}.result is required.");

        var request = new LlmTextRequest(
            StableId: requestDto.StableId ?? throw new InvalidOperationException($"Cassette {path}.request.stableId is required."),
            Intent: requestDto.Intent ?? throw new InvalidOperationException($"Cassette {path}.request.intent is required."),
            Persona: requestDto.Persona ?? throw new InvalidOperationException($"Cassette {path}.request.persona is required."),
            CanonicalContextJson: requestDto.CanonicalContextJson ?? throw new InvalidOperationException($"Cassette {path}.request.canonicalContextJson is required."),
            Sampling: ParseSampling(requestDto.Sampling, path),
            PromptTemplateVersion: requestDto.PromptTemplateVersion ?? throw new InvalidOperationException($"Cassette {path}.request.promptTemplateVersion is required."),
            OutputContractVersion: requestDto.OutputContractVersion ?? throw new InvalidOperationException($"Cassette {path}.request.outputContractVersion is required."));

        var result = new LlmTextResult(
            Text: resultDto.Text ?? throw new InvalidOperationException($"Cassette {path}.result.text is required."),
            RequestHash: resultDto.RequestHash ?? throw new InvalidOperationException($"Cassette {path}.result.requestHash is required."),
            Provider: resultDto.Provider,
            Model: resultDto.Model,
            FinishReason: resultDto.FinishReason,
            InputTokens: resultDto.InputTokens,
            OutputTokens: resultDto.OutputTokens);

        return ValidateEntry(new CassetteEntry(dto.RequestHash, request, result), path);
    }

    private static LlmSamplingOptions ParseSampling(LlmSamplingOptionsDto? samplingDto, string path)
    {
        if (samplingDto is null)
        {
            throw new InvalidOperationException($"Cassette {path}.request.sampling is required.");
        }

        return new LlmSamplingOptions(
            Provider: samplingDto.Provider ?? throw new InvalidOperationException($"Cassette {path}.request.sampling.provider is required."),
            Model: samplingDto.Model ?? throw new InvalidOperationException($"Cassette {path}.request.sampling.model is required."),
            Temperature: samplingDto.Temperature,
            MaxOutputTokens: samplingDto.MaxOutputTokens,
            TopP: samplingDto.TopP);
    }

    private static CassetteEntry ValidateEntry(CassetteEntry entry, string path)
    {
        if (string.IsNullOrWhiteSpace(entry.RequestHash))
        {
            throw new InvalidOperationException($"Cassette {path}: requestHash must be non-empty.");
        }

        if (!string.Equals(entry.RequestHash, entry.Result.RequestHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Cassette {path}: result.requestHash must match entry requestHash '{entry.RequestHash}'.");
        }

        var recomputed = LlmRequestHasher.ComputeHash(entry.Request);
        if (!string.Equals(entry.RequestHash, recomputed, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Cassette {path}: requestHash mismatch. Stored='{entry.RequestHash}', recomputed='{recomputed}'.");
        }

        return entry;
    }

    private static CassetteEntryDto ToDto(CassetteEntry entry)
        => new()
        {
            RequestHash = entry.RequestHash,
            Request = new LlmTextRequestDto
            {
                StableId = entry.Request.StableId,
                Intent = entry.Request.Intent,
                Persona = entry.Request.Persona,
                CanonicalContextJson = entry.Request.CanonicalContextJson,
                Sampling = new LlmSamplingOptionsDto
                {
                    Provider = entry.Request.Sampling.Provider,
                    Model = entry.Request.Sampling.Model,
                    Temperature = entry.Request.Sampling.Temperature,
                    MaxOutputTokens = entry.Request.Sampling.MaxOutputTokens,
                    TopP = entry.Request.Sampling.TopP,
                },
                PromptTemplateVersion = entry.Request.PromptTemplateVersion,
                OutputContractVersion = entry.Request.OutputContractVersion,
            },
            Result = new LlmTextResultDto
            {
                Text = entry.Result.Text,
                RequestHash = entry.Result.RequestHash,
                Provider = entry.Result.Provider,
                Model = entry.Result.Model,
                FinishReason = entry.Result.FinishReason,
                InputTokens = entry.Result.InputTokens,
                OutputTokens = entry.Result.OutputTokens,
            }
        };

    private sealed record CassetteEntry(string RequestHash, LlmTextRequest Request, LlmTextResult Result);

    private sealed class CassetteFileDto
    {
        public string? SchemaVersion { get; set; }

        public List<CassetteEntryDto>? Entries { get; set; }
    }

    private sealed class CassetteEntryDto
    {
        public string? RequestHash { get; set; }

        public LlmTextRequestDto? Request { get; set; }

        public LlmTextResultDto? Result { get; set; }
    }

    private sealed class LlmTextRequestDto
    {
        public string? StableId { get; set; }

        public string? Intent { get; set; }

        public string? Persona { get; set; }

        public string? CanonicalContextJson { get; set; }

        public LlmSamplingOptionsDto? Sampling { get; set; }

        public string? PromptTemplateVersion { get; set; }

        public string? OutputContractVersion { get; set; }
    }

    private sealed class LlmSamplingOptionsDto
    {
        public string? Provider { get; set; }

        public string? Model { get; set; }

        public double Temperature { get; set; }

        public int? MaxOutputTokens { get; set; }

        public double? TopP { get; set; }
    }

    private sealed class LlmTextResultDto
    {
        public string? Text { get; set; }

        public string? RequestHash { get; set; }

        public string? Provider { get; set; }

        public string? Model { get; set; }

        public string? FinishReason { get; set; }

        public int? InputTokens { get; set; }

        public int? OutputTokens { get; set; }
    }
}
