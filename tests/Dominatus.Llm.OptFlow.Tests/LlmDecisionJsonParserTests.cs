namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmDecisionJsonParserTests
{
    [Fact]
    public void DecisionJsonParser_ParsesRawJson()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);

        var result = LlmDecisionJsonParser.ParseAndValidate(ValidDecisionJson(), request, hash, "ctx");

        Assert.Equal(hash, result.RequestHash);
        Assert.Equal("negotiate", result.Scores.Single(s => s.Rank == 1).OptionId);
    }

    [Fact]
    public void DecisionJsonParser_ParsesFencedJson()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);

        var result = LlmDecisionJsonParser.ParseAndValidate($"```json\n{ValidDecisionJson()}\n```", request, hash, "ctx");

        Assert.Equal("Negotiation best fits objective.", result.Rationale);
    }

    [Fact]
    public void DecisionJsonParser_RejectsMalformedJson()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);

        var ex = Assert.Throws<InvalidOperationException>(() => LlmDecisionJsonParser.ParseAndValidate("{\"scores\":[}]", request, hash, "ctx"));

        Assert.Contains("Malformed decision JSON", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DecisionJsonParser_RejectsMultipleJsonObjects()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);

        var ex = Assert.Throws<InvalidOperationException>(() => LlmDecisionJsonParser.ParseAndValidate("{}{}", request, hash, "ctx"));

        Assert.Contains("Multiple decision JSON objects", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DecisionJsonParser_RejectsUnknownOption()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var json = """
            {
              "scores": [
                {"id":"negotiate","score":0.8,"rank":1,"rationale":"a"},
                {"id":"threaten","score":0.4,"rank":2,"rationale":"b"},
                {"id":"unknown","score":0.1,"rank":3,"rationale":"c"}
              ],
              "rationale":"x"
            }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => LlmDecisionJsonParser.ParseAndValidate(json, request, hash, "ctx"));

        Assert.Contains("missing scores", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecisionJsonParser_RejectsMissingOption()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var json = """
            {
              "scores": [
                {"id":"negotiate","score":0.8,"rank":1,"rationale":"a"},
                {"id":"threaten","score":0.4,"rank":2,"rationale":"b"}
              ],
              "rationale":"x"
            }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => LlmDecisionJsonParser.ParseAndValidate(json, request, hash, "ctx"));

        Assert.Contains("missing scores", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecisionJsonParser_RejectsRankScoreMismatch()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var json = """
            {
              "scores": [
                {"id":"negotiate","score":0.2,"rank":1,"rationale":"a"},
                {"id":"threaten","score":0.9,"rank":2,"rationale":"b"},
                {"id":"attack","score":0.1,"rank":3,"rationale":"c"}
              ],
              "rationale":"x"
            }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => LlmDecisionJsonParser.ParseAndValidate(json, request, hash, "ctx"));

        Assert.Contains("inconsistent", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecisionJsonParser_AcceptsValidDecisionJson()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);

        var result = LlmDecisionJsonParser.ParseAndValidate(ValidDecisionJson(), request, hash, "ctx");

        Assert.Equal(3, result.Scores.Count);
        Assert.Equal("attack", result.Scores.Single(s => s.Rank == 3).OptionId);
    }

    private static string ValidDecisionJson() => """
        {
          "scores": [
            {"id":"negotiate","score":0.86,"rank":1,"rationale":"Leverages guard fear."},
            {"id":"threaten","score":0.51,"rank":2,"rationale":"Could work but escalates risk."},
            {"id":"attack","score":0.18,"rank":3,"rationale":"Conflicts with low-noise objective."}
          ],
          "rationale":"Negotiation best fits objective."
        }
        """;

    private static LlmDecisionRequest CreateRequest() => new(
        StableId: "story.guard.approach.v1",
        Intent: "get past shrine guard",
        Persona: "careful infiltrator",
        CanonicalContextJson: "{\"alarmLevel\":2,\"guardMood\":\"afraid\"}",
        Options:
        [
            new LlmDecisionOption("attack", "Use force to eliminate the guard."),
            new LlmDecisionOption("negotiate", "Offer terms and persuade the guard to step aside."),
            new LlmDecisionOption("threaten", "Intimidate the guard without immediate violence."),
        ],
        Sampling: new LlmSamplingOptions("fake", "scripted-v1", Temperature: 0.0, TopP: 1.0, MaxOutputTokens: 256),
        PromptTemplateVersion: LlmDecisionRequest.DefaultPromptTemplateVersion,
        OutputContractVersion: LlmDecisionRequest.DefaultOutputContractVersion);
}
