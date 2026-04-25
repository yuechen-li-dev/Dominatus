using System.Threading;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmMagiInfrastructureTests
{
    [Fact]
    public void MagiParticipant_RejectsEmptyId() => Assert.Throws<ArgumentException>(() => Llm.MagiParticipant(" ", "openai", "gpt", "stance"));

    [Fact]
    public void MagiParticipant_RejectsEmptyProvider() => Assert.Throws<ArgumentException>(() => Llm.MagiParticipant("strategist", " ", "gpt", "stance"));

    [Fact]
    public void MagiParticipant_RejectsEmptyModel() => Assert.Throws<ArgumentException>(() => Llm.MagiParticipant("strategist", "openai", " ", "stance"));

    [Fact]
    public void MagiParticipant_RejectsEmptyStance() => Assert.Throws<ArgumentException>(() => Llm.MagiParticipant("strategist", "openai", "gpt", " "));

    [Fact]
    public void LlmMagiParticipantHelper_CreatesParticipantWithSampling()
    {
        var participant = Llm.MagiParticipant("strategist", "openai", "gpt", "strongest", temperature: 0.3, maxOutputTokens: 200, topP: 0.9);

        Assert.Equal("strategist", participant.Id);
        Assert.Equal("openai", participant.Sampling.Provider);
        Assert.Equal("gpt", participant.Sampling.Model);
        Assert.Equal(0.3, participant.Sampling.Temperature);
        Assert.Equal(200, participant.Sampling.MaxOutputTokens);
        Assert.Equal(0.9, participant.Sampling.TopP);
    }

    [Fact]
    public void MagiRequest_RejectsEmptyStableId() => Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), StableId: " "));

    [Fact]
    public void MagiRequest_RejectsTooFewOptions()
    {
        var oneOption = new[] { Llm.Option("only", "single") };
        Assert.Throws<ArgumentOutOfRangeException>(() => CloneRequest(CreateRequest(), Options: oneOption));
    }

    [Fact]
    public void MagiRequest_RejectsDuplicateOptionIds()
    {
        var options = new[] { Llm.Option("a", "one"), Llm.Option("a", "two") };
        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), Options: options));
    }

    [Fact]
    public void MagiRequest_RejectsDuplicateParticipantIds()
    {
        var same = Llm.MagiParticipant("same", "p", "m", "s");
        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), AdvocateA: same, AdvocateB: same));
    }

    [Fact]
    public void MagiRequest_RejectsEmptyVersions()
    {
        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), PromptTemplateVersion: " "));
        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), OutputContractVersion: " "));
    }

    [Fact]
    public void MagiHasher_ReturnsSameHash_ForSameRequest()
    {
        var request = CreateRequest();
        Assert.Equal(LlmMagiRequestHasher.ComputeHash(request), LlmMagiRequestHasher.ComputeHash(request));
    }

    [Fact]
    public void MagiHasher_ChangesHash_WhenAdvocateAModelChanges()
        => AssertMagiHashChanges(r => CloneRequest(
            r,
            AdvocateA: new LlmMagiParticipant(
                r.AdvocateA.Id,
                new LlmSamplingOptions("openai", "other", r.AdvocateA.Sampling.Temperature, r.AdvocateA.Sampling.MaxOutputTokens, r.AdvocateA.Sampling.TopP),
                r.AdvocateA.Stance)));

    [Fact]
    public void MagiHasher_ChangesHash_WhenAdvocateBStanceChanges()
        => AssertMagiHashChanges(r => CloneRequest(r, AdvocateB: new LlmMagiParticipant(r.AdvocateB.Id, r.AdvocateB.Sampling, "new stance")));

    [Fact]
    public void MagiHasher_ChangesHash_WhenJudgeProviderChanges()
        => AssertMagiHashChanges(r => CloneRequest(
            r,
            Judge: new LlmMagiParticipant(
                r.Judge.Id,
                new LlmSamplingOptions("other", r.Judge.Sampling.Model, r.Judge.Sampling.Temperature, r.Judge.Sampling.MaxOutputTokens, r.Judge.Sampling.TopP),
                r.Judge.Stance)));

    [Fact]
    public void MagiHasher_ChangesHash_WhenAdvocatesSwapped()
    {
        var request = CreateRequest();
        var swapped = CloneRequest(request, AdvocateA: request.AdvocateB, AdvocateB: request.AdvocateA);

        Assert.NotEqual(LlmMagiRequestHasher.ComputeHash(request), LlmMagiRequestHasher.ComputeHash(swapped));
    }

    [Fact]
    public void MagiHasher_IsStableAcrossOptionInsertionOrder()
    {
        var request = CreateRequest();
        var reversed = CloneRequest(request, Options: request.Options.Reverse().ToArray());
        Assert.Equal(LlmMagiRequestHasher.ComputeHash(request), LlmMagiRequestHasher.ComputeHash(reversed));
    }

    [Fact]
    public void MagiJudgment_RejectsUnknownChosenOption()
    {
        var request = CreateRequest();
        var judgment = new LlmMagiJudgment("unknown", request.AdvocateA.Id, "because");
        Assert.Throws<InvalidOperationException>(() => LlmMagiResultValidator.ValidateJudgmentAgainstRequest(request, judgment));
    }

    [Fact]
    public void MagiJudgment_RejectsUnknownPreferredProposalId()
    {
        var request = CreateRequest();
        var judgment = new LlmMagiJudgment("join", "somebody", "because");
        Assert.Throws<InvalidOperationException>(() => LlmMagiResultValidator.ValidateJudgmentAgainstRequest(request, judgment));
    }

    [Fact]
    public void MagiJudgment_AllowsNeitherPreferredProposalId()
    {
        var request = CreateRequest();
        var judgment = new LlmMagiJudgment("join", "neither", "because");
        LlmMagiResultValidator.ValidateJudgmentAgainstRequest(request, judgment);
    }

    [Fact]
    public void MagiJudgment_RejectsEmptyRationale() => Assert.Throws<ArgumentException>(() => new LlmMagiJudgment("join", "neither", " "));

    [Fact]
    public void MagiDecisionResult_ValidatesAdvocateResultsAgainstClosedOptions()
    {
        var request = CreateRequest();
        var hash = LlmMagiRequestHasher.ComputeHash(request);
        var advocateARequest = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var advocateAHash = LlmDecisionRequestHasher.ComputeHash(advocateARequest);
        var badA = new LlmDecisionResult(advocateAHash, [
            new LlmDecisionOptionScore("join", 0.8, 1, "ok"),
            new LlmDecisionOptionScore("refuse", 0.2, 2, "ok")], "bad");

        var advocateBRequest = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);
        var advocateBHash = LlmDecisionRequestHasher.ComputeHash(advocateBRequest);
        var goodB = CreateDecisionResult(advocateBHash);

        var result = new LlmMagiDecisionResult(hash, request.AdvocateA, request.AdvocateB, request.Judge, badA, goodB, new LlmMagiJudgment("join", request.AdvocateA.Id, "because"));
        Assert.Throws<InvalidOperationException>(() => LlmMagiResultValidator.ValidateDecisionResultAgainstRequest(request, hash, result));
    }

    [Fact]
    public async Task FakeMagiJudgeClient_ReturnsConfiguredJudgment()
    {
        var request = CreateRequest();
        var judgment = new LlmMagiJudgment("join", request.AdvocateA.Id, "because");
        var client = new FakeLlmMagiJudgeClient(judgment);
        var a = CreateDecisionResult("a");
        var b = CreateDecisionResult("b");

        var actual = await client.JudgeAsync(request, "hash", a, b, CancellationToken.None);
        Assert.Equal(judgment, actual);
    }

    [Fact]
    public async Task FakeMagiJudgeClient_IncrementsCallCount()
    {
        var request = CreateRequest();
        var client = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "because"));

        await client.JudgeAsync(request, "hash", CreateDecisionResult("a"), CreateDecisionResult("b"), CancellationToken.None);
        await client.JudgeAsync(request, "hash", CreateDecisionResult("a"), CreateDecisionResult("b"), CancellationToken.None);

        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task FakeMagiJudgeClient_RecordsInputs()
    {
        var request = CreateRequest();
        var client = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "because"));
        var a = CreateDecisionResult("a");
        var b = CreateDecisionResult("b");

        await client.JudgeAsync(request, "hash", a, b, CancellationToken.None);

        Assert.Same(request, client.LastRequest);
        Assert.Equal("hash", client.LastRequestHash);
        Assert.Same(a, client.LastAdvocateAResult);
        Assert.Same(b, client.LastAdvocateBResult);
    }

    [Fact]
    public async Task FakeMagiJudgeClient_HonorsCancellation()
    {
        var request = CreateRequest();
        var client = new FakeLlmMagiJudgeClient(new LlmMagiJudgment("join", request.AdvocateA.Id, "because"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.JudgeAsync(request, "hash", CreateDecisionResult("a"), CreateDecisionResult("b"), cts.Token));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void InMemoryMagiCassette_ReturnsStoredEntry()
    {
        var request = CreateRequest();
        var hash = LlmMagiRequestHasher.ComputeHash(request);
        var result = CreateMagiResult(request, hash);
        var cassette = new InMemoryLlmMagiCassette();
        cassette.Put(hash, request, result);

        Assert.True(cassette.TryGet(hash, out var stored));
        Assert.Equal(result, stored);
    }

    [Fact]
    public void InMemoryMagiCassette_ReturnsFalseForMissingHash()
    {
        var cassette = new InMemoryLlmMagiCassette();
        Assert.False(cassette.TryGet("missing", out _));
    }

    [Fact]
    public void InMemoryMagiCassette_AllowsIdempotentPut()
    {
        var request = CreateRequest();
        var hash = LlmMagiRequestHasher.ComputeHash(request);
        var result = CreateMagiResult(request, hash);
        var cassette = new InMemoryLlmMagiCassette();

        cassette.Put(hash, request, result);
        cassette.Put(hash, request, result);

        Assert.True(cassette.TryGet(hash, out var stored));
        Assert.Equal(result, stored);
    }

    [Fact]
    public void InMemoryMagiCassette_RejectsSameHashWithDifferentResult()
    {
        var request = CreateRequest();
        var hash = LlmMagiRequestHasher.ComputeHash(request);
        var cassette = new InMemoryLlmMagiCassette();
        cassette.Put(hash, request, CreateMagiResult(request, hash));

        var other = new LlmMagiDecisionResult(hash, request.AdvocateA, request.AdvocateB, request.Judge, CreateDecisionResult("aa"), CreateDecisionResult("bb"), new LlmMagiJudgment("join", "neither", "other"));
        Assert.Throws<InvalidOperationException>(() => cassette.Put(hash, request, other));
    }

    private static void AssertMagiHashChanges(Func<LlmMagiRequest, LlmMagiRequest> mutate)
    {
        var original = CreateRequest();
        var changed = mutate(original);
        Assert.NotEqual(LlmMagiRequestHasher.ComputeHash(original), LlmMagiRequestHasher.ComputeHash(changed));
    }

    private static LlmMagiRequest CreateRequest() => new(
        StableId: "gandhi.war-council.v1",
        Intent: "decide whether Gandhi should join Victoria's war against Alexander",
        Persona: "Gandhi",
        CanonicalContextJson: "{\"a\":1}",
        Options: [Llm.Option("join", "Join"), Llm.Option("refuse", "Refuse"), Llm.Option("mediate", "Mediate")],
        AdvocateA: Llm.MagiParticipant("strategist", "openai", "gpt", "strategy"),
        AdvocateB: Llm.MagiParticipant("character", "anthropic", "claude", "character"),
        Judge: Llm.MagiParticipant("judge", "gemini", "gemini", "judge"),
        PromptTemplateVersion: LlmMagiRequest.DefaultPromptTemplateVersion,
        OutputContractVersion: LlmMagiRequest.DefaultOutputContractVersion);

    private static LlmMagiRequest CloneRequest(
        LlmMagiRequest source,
        string? StableId = null,
        IReadOnlyList<LlmDecisionOption>? Options = null,
        LlmMagiParticipant? AdvocateA = null,
        LlmMagiParticipant? AdvocateB = null,
        LlmMagiParticipant? Judge = null,
        string? PromptTemplateVersion = null,
        string? OutputContractVersion = null)
        => new(
            StableId: StableId ?? source.StableId,
            Intent: source.Intent,
            Persona: source.Persona,
            CanonicalContextJson: source.CanonicalContextJson,
            Options: Options ?? source.Options,
            AdvocateA: AdvocateA ?? source.AdvocateA,
            AdvocateB: AdvocateB ?? source.AdvocateB,
            Judge: Judge ?? source.Judge,
            PromptTemplateVersion: PromptTemplateVersion ?? source.PromptTemplateVersion,
            OutputContractVersion: OutputContractVersion ?? source.OutputContractVersion);

    private static LlmDecisionResult CreateDecisionResult(string hash)
        => new(hash, [
            new LlmDecisionOptionScore("join", 0.8, 1, "a"),
            new LlmDecisionOptionScore("mediate", 0.5, 2, "b"),
            new LlmDecisionOptionScore("refuse", 0.2, 3, "c")], "overall");

    private static LlmMagiDecisionResult CreateMagiResult(LlmMagiRequest request, string requestHash)
    {
        var aReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var bReq = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);
        return new LlmMagiDecisionResult(
            requestHash,
            request.AdvocateA,
            request.AdvocateB,
            request.Judge,
            CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(aReq)),
            CreateDecisionResult(LlmDecisionRequestHasher.ComputeHash(bReq)),
            new LlmMagiJudgment("join", request.AdvocateA.Id, "because"));
    }
}
