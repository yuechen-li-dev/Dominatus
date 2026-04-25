using System.Text;
using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

public sealed class JsonLlmMagiCassette : ILlmMagiCassette
{
    private const string SchemaVersion = "dom.llm.magi_cassette.v1";

    private readonly string _path;
    private readonly Dictionary<string, CassetteEntry> _entries;

    public JsonLlmMagiCassette(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _entries = new Dictionary<string, CassetteEntry>(StringComparer.Ordinal);
    }

    private JsonLlmMagiCassette(string path, Dictionary<string, CassetteEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _entries = entries;
    }

    public static JsonLlmMagiCassette LoadOrCreate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return new JsonLlmMagiCassette(path);
        }

        var jsonText = File.ReadAllText(path, Encoding.UTF8);

        CassetteFileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<CassetteFileDto>(jsonText, CreateSerializerOptions(writeIndented: false));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Magi cassette JSON at '{path}' is malformed. {ex.Message}", ex);
        }

        if (dto is null)
        {
            throw new InvalidOperationException($"Magi cassette JSON at '{path}' is empty or invalid.");
        }

        if (!string.Equals(dto.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unsupported magi cassette schemaVersion '{dto.SchemaVersion ?? "<null>"}' at '{path}'. Expected '{SchemaVersion}'.");
        }

        if (dto.Entries is null)
        {
            throw new InvalidOperationException($"Magi cassette JSON at '{path}' is missing required property 'entries'.");
        }

        var entries = new Dictionary<string, CassetteEntry>(StringComparer.Ordinal);

        for (int i = 0; i < dto.Entries.Count; i++)
        {
            var entryDto = dto.Entries[i] ?? throw new InvalidOperationException($"Magi cassette entry at index {i} is null.");
            var entry = ParseAndValidateEntry(entryDto, $"entries[{i}]");

            if (!entries.TryAdd(entry.RequestHash, entry))
            {
                throw new InvalidOperationException($"Magi cassette contains duplicate requestHash '{entry.RequestHash}' at entries[{i}].");
            }
        }

        return new JsonLlmMagiCassette(path, entries);
    }

    public bool TryGet(string requestHash, out LlmMagiDecisionResult result)
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

    public void Put(string requestHash, LlmMagiRequest request, LlmMagiDecisionResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        var validated = ValidateEntry(new CassetteEntry(requestHash, request, result), "put");

        if (_entries.TryGetValue(requestHash, out var existing))
        {
            if (!RequestSemanticallyEqual(existing.Request, validated.Request))
            {
                throw new InvalidOperationException($"Duplicate hash '{requestHash}' has mismatched request metadata.");
            }

            if (DecisionResultSemanticallyEqual(existing.Result, validated.Result))
            {
                return;
            }

            throw new InvalidOperationException($"Duplicate hash '{requestHash}' has a different magi decision result payload.");
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
            throw new InvalidOperationException($"Magi cassette {path}.requestHash must be non-empty.");
        }

        var requestDto = dto.Request ?? throw new InvalidOperationException($"Magi cassette {path}.request is required.");
        var resultDto = dto.Result ?? throw new InvalidOperationException($"Magi cassette {path}.result is required.");

        var request = ParseRequest(requestDto, path);
        var result = ParseResult(resultDto, path);

        return ValidateEntry(new CassetteEntry(dto.RequestHash, request, result), path);
    }

    private static LlmMagiRequest ParseRequest(LlmMagiRequestDto requestDto, string path)
    {
        var optionsDto = requestDto.Options ?? throw new InvalidOperationException($"Magi cassette {path}.request.options is required.");
        var options = optionsDto
            .Select((o, i) => new LlmDecisionOption(
                o?.Id ?? throw new InvalidOperationException($"Magi cassette {path}.request.options[{i}].id is required."),
                o.Description ?? throw new InvalidOperationException($"Magi cassette {path}.request.options[{i}].description is required.")))
            .ToArray();

        return new LlmMagiRequest(
            StableId: requestDto.StableId ?? throw new InvalidOperationException($"Magi cassette {path}.request.stableId is required."),
            Intent: requestDto.Intent ?? throw new InvalidOperationException($"Magi cassette {path}.request.intent is required."),
            Persona: requestDto.Persona ?? throw new InvalidOperationException($"Magi cassette {path}.request.persona is required."),
            CanonicalContextJson: requestDto.CanonicalContextJson ?? throw new InvalidOperationException($"Magi cassette {path}.request.canonicalContextJson is required."),
            Options: options,
            AdvocateA: ParseParticipant(requestDto.AdvocateA, path, "advocateA"),
            AdvocateB: ParseParticipant(requestDto.AdvocateB, path, "advocateB"),
            Judge: ParseParticipant(requestDto.Judge, path, "judge"),
            PromptTemplateVersion: requestDto.PromptTemplateVersion ?? throw new InvalidOperationException($"Magi cassette {path}.request.promptTemplateVersion is required."),
            OutputContractVersion: requestDto.OutputContractVersion ?? throw new InvalidOperationException($"Magi cassette {path}.request.outputContractVersion is required."));
    }

    private static LlmMagiParticipant ParseParticipant(LlmMagiParticipantDto? participantDto, string path, string role)
    {
        if (participantDto is null)
        {
            throw new InvalidOperationException($"Magi cassette {path}.request.{role} is required.");
        }

        return new LlmMagiParticipant(
            Id: participantDto.Id ?? throw new InvalidOperationException($"Magi cassette {path}.request.{role}.id is required."),
            Sampling: ParseSampling(participantDto.Sampling, path, role),
            Stance: participantDto.Stance ?? throw new InvalidOperationException($"Magi cassette {path}.request.{role}.stance is required."));
    }

    private static LlmSamplingOptions ParseSampling(LlmSamplingOptionsDto? samplingDto, string path, string role)
    {
        if (samplingDto is null)
        {
            throw new InvalidOperationException($"Magi cassette {path}.request.{role}.sampling is required.");
        }

        return new LlmSamplingOptions(
            Provider: samplingDto.Provider ?? throw new InvalidOperationException($"Magi cassette {path}.request.{role}.sampling.provider is required."),
            Model: samplingDto.Model ?? throw new InvalidOperationException($"Magi cassette {path}.request.{role}.sampling.model is required."),
            Temperature: samplingDto.Temperature,
            MaxOutputTokens: samplingDto.MaxOutputTokens,
            TopP: samplingDto.TopP);
    }

    private static LlmMagiDecisionResult ParseResult(LlmMagiDecisionResultDto resultDto, string path)
    {
        var advocateAResult = ParseDecisionResult(resultDto.AdvocateAResult, path, "advocateAResult");
        var advocateBResult = ParseDecisionResult(resultDto.AdvocateBResult, path, "advocateBResult");

        return new LlmMagiDecisionResult(
            RequestHash: resultDto.RequestHash ?? throw new InvalidOperationException($"Magi cassette {path}.result.requestHash is required."),
            AdvocateA: ParseParticipant(resultDto.AdvocateA, path, "result.advocateA"),
            AdvocateB: ParseParticipant(resultDto.AdvocateB, path, "result.advocateB"),
            Judge: ParseParticipant(resultDto.Judge, path, "result.judge"),
            AdvocateAResult: advocateAResult,
            AdvocateBResult: advocateBResult,
            Judgment: ParseJudgment(resultDto.Judgment, path));
    }

    private static LlmDecisionResult ParseDecisionResult(LlmDecisionResultDto? decisionResultDto, string path, string propertyName)
    {
        if (decisionResultDto is null)
        {
            throw new InvalidOperationException($"Magi cassette {path}.result.{propertyName} is required.");
        }

        var scoresDto = decisionResultDto.Scores ?? throw new InvalidOperationException($"Magi cassette {path}.result.{propertyName}.scores is required.");
        var scores = scoresDto
            .Select((s, i) => new LlmDecisionOptionScore(
                s?.OptionId ?? throw new InvalidOperationException($"Magi cassette {path}.result.{propertyName}.scores[{i}].optionId is required."),
                s.Score,
                s.Rank,
                s.Rationale ?? throw new InvalidOperationException($"Magi cassette {path}.result.{propertyName}.scores[{i}].rationale is required.")))
            .ToArray();

        return new LlmDecisionResult(
            RequestHash: decisionResultDto.RequestHash ?? throw new InvalidOperationException($"Magi cassette {path}.result.{propertyName}.requestHash is required."),
            Scores: scores,
            Rationale: decisionResultDto.Rationale ?? throw new InvalidOperationException($"Magi cassette {path}.result.{propertyName}.rationale is required."));
    }

    private static LlmMagiJudgment ParseJudgment(LlmMagiJudgmentDto? judgmentDto, string path)
    {
        if (judgmentDto is null)
        {
            throw new InvalidOperationException($"Magi cassette {path}.result.judgment is required.");
        }

        return new LlmMagiJudgment(
            ChosenOptionId: judgmentDto.ChosenOptionId ?? throw new InvalidOperationException($"Magi cassette {path}.result.judgment.chosenOptionId is required."),
            PreferredProposalId: judgmentDto.PreferredProposalId ?? throw new InvalidOperationException($"Magi cassette {path}.result.judgment.preferredProposalId is required."),
            Rationale: judgmentDto.Rationale ?? throw new InvalidOperationException($"Magi cassette {path}.result.judgment.rationale is required."));
    }

    private static CassetteEntry ValidateEntry(CassetteEntry entry, string path)
    {
        if (string.IsNullOrWhiteSpace(entry.RequestHash))
        {
            throw new InvalidOperationException($"Magi cassette {path}: requestHash must be non-empty.");
        }

        var recomputed = LlmMagiRequestHasher.ComputeHash(entry.Request);
        if (!string.Equals(entry.RequestHash, recomputed, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Magi cassette {path}: requestHash mismatch. Stored='{entry.RequestHash}', recomputed='{recomputed}'.");
        }

        if (!string.Equals(entry.RequestHash, entry.Result.RequestHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Magi cassette {path}: result.requestHash must match entry requestHash '{entry.RequestHash}'.");
        }

        LlmMagiResultValidator.ValidateDecisionResultAgainstRequest(entry.Request, entry.RequestHash, entry.Result);
        return entry;
    }

    private static bool RequestSemanticallyEqual(LlmMagiRequest left, LlmMagiRequest right)
    {
        return string.Equals(left.StableId, right.StableId, StringComparison.Ordinal)
            && string.Equals(left.Intent, right.Intent, StringComparison.Ordinal)
            && string.Equals(left.Persona, right.Persona, StringComparison.Ordinal)
            && string.Equals(left.CanonicalContextJson, right.CanonicalContextJson, StringComparison.Ordinal)
            && string.Equals(left.PromptTemplateVersion, right.PromptTemplateVersion, StringComparison.Ordinal)
            && string.Equals(left.OutputContractVersion, right.OutputContractVersion, StringComparison.Ordinal)
            && ParticipantEqual(left.AdvocateA, right.AdvocateA)
            && ParticipantEqual(left.AdvocateB, right.AdvocateB)
            && ParticipantEqual(left.Judge, right.Judge)
            && left.Options.OrderBy(o => o.Id, StringComparer.Ordinal)
                .SequenceEqual(right.Options.OrderBy(o => o.Id, StringComparer.Ordinal));
    }

    private static bool DecisionResultSemanticallyEqual(LlmMagiDecisionResult left, LlmMagiDecisionResult right)
    {
        return string.Equals(left.RequestHash, right.RequestHash, StringComparison.Ordinal)
            && ParticipantEqual(left.AdvocateA, right.AdvocateA)
            && ParticipantEqual(left.AdvocateB, right.AdvocateB)
            && ParticipantEqual(left.Judge, right.Judge)
            && DecisionResultEqual(left.AdvocateAResult, right.AdvocateAResult)
            && DecisionResultEqual(left.AdvocateBResult, right.AdvocateBResult)
            && left.Judgment == right.Judgment;
    }

    private static bool DecisionResultEqual(LlmDecisionResult left, LlmDecisionResult right)
    {
        return string.Equals(left.RequestHash, right.RequestHash, StringComparison.Ordinal)
            && string.Equals(left.Rationale, right.Rationale, StringComparison.Ordinal)
            && left.Scores.OrderBy(s => s.OptionId, StringComparer.Ordinal)
                .SequenceEqual(right.Scores.OrderBy(s => s.OptionId, StringComparer.Ordinal));
    }

    private static bool ParticipantEqual(LlmMagiParticipant left, LlmMagiParticipant right)
        => left == right;

    private static CassetteEntryDto ToDto(CassetteEntry entry)
        => new()
        {
            RequestHash = entry.RequestHash,
            Request = ToDto(entry.Request),
            Result = ToDto(entry.Result)
        };

    private static LlmMagiRequestDto ToDto(LlmMagiRequest request)
        => new()
        {
            StableId = request.StableId,
            Intent = request.Intent,
            Persona = request.Persona,
            CanonicalContextJson = request.CanonicalContextJson,
            Options = request.Options
                .OrderBy(o => o.Id, StringComparer.Ordinal)
                .Select(o => new LlmDecisionOptionDto { Id = o.Id, Description = o.Description })
                .ToList(),
            AdvocateA = ToDto(request.AdvocateA),
            AdvocateB = ToDto(request.AdvocateB),
            Judge = ToDto(request.Judge),
            PromptTemplateVersion = request.PromptTemplateVersion,
            OutputContractVersion = request.OutputContractVersion,
        };

    private static LlmMagiParticipantDto ToDto(LlmMagiParticipant participant)
        => new()
        {
            Id = participant.Id,
            Sampling = new LlmSamplingOptionsDto
            {
                Provider = participant.Sampling.Provider,
                Model = participant.Sampling.Model,
                Temperature = participant.Sampling.Temperature,
                MaxOutputTokens = participant.Sampling.MaxOutputTokens,
                TopP = participant.Sampling.TopP,
            },
            Stance = participant.Stance,
        };

    private static LlmMagiDecisionResultDto ToDto(LlmMagiDecisionResult result)
        => new()
        {
            RequestHash = result.RequestHash,
            AdvocateA = ToDto(result.AdvocateA),
            AdvocateB = ToDto(result.AdvocateB),
            Judge = ToDto(result.Judge),
            AdvocateAResult = ToDto(result.AdvocateAResult),
            AdvocateBResult = ToDto(result.AdvocateBResult),
            Judgment = new LlmMagiJudgmentDto
            {
                ChosenOptionId = result.Judgment.ChosenOptionId,
                PreferredProposalId = result.Judgment.PreferredProposalId,
                Rationale = result.Judgment.Rationale,
            }
        };

    private static LlmDecisionResultDto ToDto(LlmDecisionResult result)
        => new()
        {
            RequestHash = result.RequestHash,
            Scores = result.Scores
                .OrderBy(s => s.OptionId, StringComparer.Ordinal)
                .Select(s => new LlmDecisionOptionScoreDto
                {
                    OptionId = s.OptionId,
                    Score = s.Score,
                    Rank = s.Rank,
                    Rationale = s.Rationale,
                })
                .ToList(),
            Rationale = result.Rationale,
        };

    private sealed record CassetteEntry(string RequestHash, LlmMagiRequest Request, LlmMagiDecisionResult Result);

    private sealed class CassetteFileDto
    {
        public string? SchemaVersion { get; set; }

        public List<CassetteEntryDto>? Entries { get; set; }
    }

    private sealed class CassetteEntryDto
    {
        public string? RequestHash { get; set; }

        public LlmMagiRequestDto? Request { get; set; }

        public LlmMagiDecisionResultDto? Result { get; set; }
    }

    private sealed class LlmMagiRequestDto
    {
        public string? StableId { get; set; }

        public string? Intent { get; set; }

        public string? Persona { get; set; }

        public string? CanonicalContextJson { get; set; }

        public List<LlmDecisionOptionDto>? Options { get; set; }

        public LlmMagiParticipantDto? AdvocateA { get; set; }

        public LlmMagiParticipantDto? AdvocateB { get; set; }

        public LlmMagiParticipantDto? Judge { get; set; }

        public string? PromptTemplateVersion { get; set; }

        public string? OutputContractVersion { get; set; }
    }

    private sealed class LlmDecisionOptionDto
    {
        public string? Id { get; set; }

        public string? Description { get; set; }
    }

    private sealed class LlmMagiParticipantDto
    {
        public string? Id { get; set; }

        public LlmSamplingOptionsDto? Sampling { get; set; }

        public string? Stance { get; set; }
    }

    private sealed class LlmSamplingOptionsDto
    {
        public string? Provider { get; set; }

        public string? Model { get; set; }

        public double Temperature { get; set; }

        public int? MaxOutputTokens { get; set; }

        public double? TopP { get; set; }
    }

    private sealed class LlmMagiDecisionResultDto
    {
        public string? RequestHash { get; set; }

        public LlmMagiParticipantDto? AdvocateA { get; set; }

        public LlmMagiParticipantDto? AdvocateB { get; set; }

        public LlmMagiParticipantDto? Judge { get; set; }

        public LlmDecisionResultDto? AdvocateAResult { get; set; }

        public LlmDecisionResultDto? AdvocateBResult { get; set; }

        public LlmMagiJudgmentDto? Judgment { get; set; }
    }

    private sealed class LlmDecisionResultDto
    {
        public string? RequestHash { get; set; }

        public List<LlmDecisionOptionScoreDto>? Scores { get; set; }

        public string? Rationale { get; set; }
    }

    private sealed class LlmDecisionOptionScoreDto
    {
        public string? OptionId { get; set; }

        public double Score { get; set; }

        public int Rank { get; set; }

        public string? Rationale { get; set; }
    }

    private sealed class LlmMagiJudgmentDto
    {
        public string? ChosenOptionId { get; set; }

        public string? PreferredProposalId { get; set; }

        public string? Rationale { get; set; }
    }
}
