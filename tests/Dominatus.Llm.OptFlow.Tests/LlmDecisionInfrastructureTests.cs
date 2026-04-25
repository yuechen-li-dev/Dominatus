using System.Threading;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmDecisionInfrastructureTests
{
    [Fact]
    public void DecisionOption_RejectsEmptyId()
    {
        Assert.Throws<ArgumentException>(() => new LlmDecisionOption(" ", "desc"));
    }

    [Fact]
    public void DecisionOption_RejectsEmptyDescription()
    {
        Assert.Throws<ArgumentException>(() => new LlmDecisionOption("opt", " "));
    }

    [Fact]
    public void DecisionRequest_RejectsEmptyStableId()
    {
        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), StableId: " "));
    }

    [Fact]
    public void DecisionRequest_RejectsEmptyIntent()
    {
        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), Intent: " "));
    }

    [Fact]
    public void DecisionRequest_RejectsEmptyPersona()
    {
        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), Persona: " "));
    }

    [Fact]
    public void DecisionRequest_RejectsTooFewOptions()
    {
        var oneOption = new[] { new LlmDecisionOption("only", "single") };
        Assert.Throws<ArgumentOutOfRangeException>(() => CloneRequest(CreateRequest(), Options: oneOption));
    }

    [Fact]
    public void DecisionRequest_RejectsDuplicateOptionIds()
    {
        var options = new[]
        {
            new LlmDecisionOption("attack", "Violence"),
            new LlmDecisionOption("attack", "Duplicate"),
        };

        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), Options: options));
    }

    [Fact]
    public void DecisionRequest_RejectsEmptyContext()
    {
        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), CanonicalContextJson: " "));
    }

    [Fact]
    public void DecisionRequest_RejectsEmptyVersions()
    {
        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), PromptTemplateVersion: " "));
        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), OutputContractVersion: " "));
    }

    [Fact]
    public void DecisionHasher_ReturnsSameHash_ForSameRequest()
    {
        var request = CreateRequest();
        var h1 = LlmDecisionRequestHasher.ComputeHash(request);
        var h2 = LlmDecisionRequestHasher.ComputeHash(request);
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void DecisionHasher_ChangesHash_WhenIntentChanges()
        => AssertDecisionHashChanges(r => CloneRequest(r, Intent: "new intent"));

    [Fact]
    public void DecisionHasher_ChangesHash_WhenPersonaChanges()
        => AssertDecisionHashChanges(r => CloneRequest(r, Persona: "new persona"));

    [Fact]
    public void DecisionHasher_ChangesHash_WhenContextChanges()
        => AssertDecisionHashChanges(r => CloneRequest(r, CanonicalContextJson: "{\"x\":1}"));

    [Fact]
    public void DecisionHasher_ChangesHash_WhenOptionDescriptionChanges()
        => AssertDecisionHashChanges(r => CloneRequest(
            r,
            Options: r.Options.Select(o => o.Id == "negotiate" ? new LlmDecisionOption(o.Id, "updated") : o).ToArray()));

    [Fact]
    public void DecisionHasher_ChangesHash_WhenSamplingChanges()
        => AssertDecisionHashChanges(r => CloneRequest(r, Sampling: new LlmSamplingOptions(r.Sampling.Provider, r.Sampling.Model, Temperature: 0.3, TopP: r.Sampling.TopP, MaxOutputTokens: r.Sampling.MaxOutputTokens)));

    [Fact]
    public void DecisionHasher_IsStableAcrossOptionInsertionOrder()
    {
        var requestA = CreateRequest();
        var requestB = CloneRequest(
            requestA,
            Options: requestA.Options.Reverse().ToArray());

        var h1 = LlmDecisionRequestHasher.ComputeHash(requestA);
        var h2 = LlmDecisionRequestHasher.ComputeHash(requestB);

        Assert.Equal(h1, h2);
    }

    [Fact]
    public void DecisionResult_RejectsMissingOptionScore()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var result = new LlmDecisionResult(hash, [
            new LlmDecisionOptionScore("negotiate", 0.9, 1, "best"),
            new LlmDecisionOptionScore("threaten", 0.5, 2, "mid")], "overall");

        Assert.Throws<InvalidOperationException>(() => LlmDecisionResultValidator.ValidateAgainstRequest(request, hash, result));
    }

    [Fact]
    public void DecisionResult_RejectsUnknownOptionScore()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var result = new LlmDecisionResult(hash, [
            new LlmDecisionOptionScore("negotiate", 0.9, 1, "best"),
            new LlmDecisionOptionScore("threaten", 0.5, 2, "mid"),
            new LlmDecisionOptionScore("flee", 0.3, 3, "unknown")], "overall");

        Assert.Throws<InvalidOperationException>(() => LlmDecisionResultValidator.ValidateAgainstRequest(request, hash, result));
    }

    [Fact]
    public void DecisionResult_RejectsDuplicateOptionScore()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var result = new LlmDecisionResult(hash, [
            new LlmDecisionOptionScore("negotiate", 0.9, 1, "best"),
            new LlmDecisionOptionScore("negotiate", 0.5, 2, "dup"),
            new LlmDecisionOptionScore("attack", 0.1, 3, "worst")], "overall");

        Assert.Throws<InvalidOperationException>(() => LlmDecisionResultValidator.ValidateAgainstRequest(request, hash, result));
    }

    [Fact]
    public void DecisionResult_RejectsScoreOutsideRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlmDecisionOptionScore("negotiate", 1.2, 1, "bad"));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void DecisionResult_RejectsNaNOrInfinityScore(double score)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlmDecisionOptionScore("negotiate", score, 1, "bad"));
    }

    [Fact]
    public void DecisionResult_RejectsInvalidRanks()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var result = new LlmDecisionResult(hash, [
            new LlmDecisionOptionScore("negotiate", 0.9, 1, "best"),
            new LlmDecisionOptionScore("threaten", 0.5, 1, "dup rank"),
            new LlmDecisionOptionScore("attack", 0.1, 3, "worst")], "overall");

        Assert.Throws<InvalidOperationException>(() => LlmDecisionResultValidator.ValidateAgainstRequest(request, hash, result));
    }

    [Fact]
    public void DecisionResult_RejectsEmptyRationale()
    {
        Assert.Throws<ArgumentException>(() => new LlmDecisionOptionScore("x", 0.4, 1, " "));
        Assert.Throws<ArgumentException>(() => new LlmDecisionResult("hash", [new LlmDecisionOptionScore("x", 0.4, 1, "ok")], " "));
    }

    [Fact]
    public void DecisionResult_RejectsRankScoreMismatch()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var result = new LlmDecisionResult(hash, [
            new LlmDecisionOptionScore("negotiate", 0.7, 2, "better"),
            new LlmDecisionOptionScore("threaten", 0.6, 1, "worse than negotiate"),
            new LlmDecisionOptionScore("attack", 0.1, 3, "worst")], "overall");

        Assert.Throws<InvalidOperationException>(() => LlmDecisionResultValidator.ValidateAgainstRequest(request, hash, result));
    }

    [Fact]
    public void DecisionResult_AcceptsValidScoresAndRationales()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var result = CreateValidResult(hash);

        LlmDecisionResultValidator.ValidateAgainstRequest(request, hash, result);
    }

    [Fact]
    public async Task FakeDecisionClient_ReturnsConfiguredResult()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var expected = CreateValidResult(hash);
        var client = new FakeLlmDecisionClient(expected);

        var result = await client.ScoreOptionsAsync(request, hash, CancellationToken.None);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task FakeDecisionClient_IncrementsCallCount()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var client = new FakeLlmDecisionClient(CreateValidResult(hash));

        await client.ScoreOptionsAsync(request, hash, CancellationToken.None);
        await client.ScoreOptionsAsync(request, hash, CancellationToken.None);

        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task FakeDecisionClient_RecordsLastRequestAndHash()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var client = new FakeLlmDecisionClient(CreateValidResult(hash));

        await client.ScoreOptionsAsync(request, hash, CancellationToken.None);

        Assert.Same(request, client.LastRequest);
        Assert.Equal(hash, client.LastRequestHash);
    }

    [Fact]
    public async Task FakeDecisionClient_HonorsCancellation()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var client = new FakeLlmDecisionClient(CreateValidResult(hash));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ScoreOptionsAsync(request, hash, cts.Token));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void InMemoryDecisionCassette_ReturnsStoredEntry()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var result = CreateValidResult(hash);
        var cassette = new InMemoryLlmDecisionCassette();

        cassette.Put(hash, request, result);

        Assert.True(cassette.TryGet(hash, out var stored));
        Assert.Equal(result, stored);
    }

    [Fact]
    public void InMemoryDecisionCassette_ReturnsFalseForMissingHash()
    {
        var cassette = new InMemoryLlmDecisionCassette();
        Assert.False(cassette.TryGet("missing", out _));
    }

    [Fact]
    public void InMemoryDecisionCassette_AllowsIdempotentPut()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var result = CreateValidResult(hash);
        var cassette = new InMemoryLlmDecisionCassette();

        cassette.Put(hash, request, result);
        cassette.Put(hash, request, result);

        Assert.True(cassette.TryGet(hash, out var stored));
        Assert.Equal(result, stored);
    }

    [Fact]
    public void InMemoryDecisionCassette_RejectsSameHashWithDifferentResult()
    {
        var request = CreateRequest();
        var hash = LlmDecisionRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmDecisionCassette();

        cassette.Put(hash, request, CreateValidResult(hash));

        var different = new LlmDecisionResult(hash, [
            new LlmDecisionOptionScore("negotiate", 0.5, 1, "different"),
            new LlmDecisionOptionScore("threaten", 0.4, 2, "different"),
            new LlmDecisionOptionScore("attack", 0.3, 3, "different")], "different");

        Assert.Throws<InvalidOperationException>(() => cassette.Put(hash, request, different));
    }

    private static void AssertDecisionHashChanges(Func<LlmDecisionRequest, LlmDecisionRequest> mutate)
    {
        var original = CreateRequest();
        var changed = mutate(original);

        var h1 = LlmDecisionRequestHasher.ComputeHash(original);
        var h2 = LlmDecisionRequestHasher.ComputeHash(changed);

        Assert.NotEqual(h1, h2);
    }

    private static LlmDecisionResult CreateValidResult(string requestHash)
        => new(
            requestHash,
            [
                new LlmDecisionOptionScore("negotiate", 0.86, 1, "Fearful guard responds to social leverage."),
                new LlmDecisionOptionScore("threaten", 0.51, 2, "Might work but risks escalation."),
                new LlmDecisionOptionScore("attack", 0.18, 3, "High risk and misaligned with goal.")
            ],
            "Negotiation best aligns with goal and current social context.");

    private static LlmDecisionRequest CreateRequest() => new(
        StableId: "story.guard.approach.v1",
        Intent: "get past shrine guard",
        Persona: "careful infiltrator",
        CanonicalContextJson: "{\"alarmLevel\":2,\"guardMood\":\"afraid\"}",
        Options: [
            new LlmDecisionOption("attack", "Use force to eliminate the guard."),
            new LlmDecisionOption("negotiate", "Offer terms and persuade the guard to step aside."),
            new LlmDecisionOption("threaten", "Intimidate the guard without immediate violence.")],
        Sampling: new LlmSamplingOptions("fake", "scripted-v1", Temperature: 0.0, TopP: 1.0, MaxOutputTokens: 256),
        PromptTemplateVersion: LlmDecisionRequest.DefaultPromptTemplateVersion,
        OutputContractVersion: LlmDecisionRequest.DefaultOutputContractVersion);

    private static LlmDecisionRequest CloneRequest(
        LlmDecisionRequest source,
        string? StableId = null,
        string? Intent = null,
        string? Persona = null,
        string? CanonicalContextJson = null,
        IReadOnlyList<LlmDecisionOption>? Options = null,
        LlmSamplingOptions? Sampling = null,
        string? PromptTemplateVersion = null,
        string? OutputContractVersion = null)
        => new(
            StableId: StableId ?? source.StableId,
            Intent: Intent ?? source.Intent,
            Persona: Persona ?? source.Persona,
            CanonicalContextJson: CanonicalContextJson ?? source.CanonicalContextJson,
            Options: Options ?? source.Options,
            Sampling: Sampling ?? source.Sampling,
            PromptTemplateVersion: PromptTemplateVersion ?? source.PromptTemplateVersion,
            OutputContractVersion: OutputContractVersion ?? source.OutputContractVersion);
}
