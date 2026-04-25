using System.Threading;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmInfrastructureTests
{
    [Fact]
    public void ContextBuilder_ProducesSameJson_ForDifferentInsertionOrder()
    {
        var a = new LlmContextBuilder()
            .Add("playerName", "Mira")
            .Add("location", "moonlit shrine")
            .Add("oracleMood", "pleased but ominous")
            .BuildCanonicalJson();

        var b = new LlmContextBuilder()
            .Add("oracleMood", "pleased but ominous")
            .Add("playerName", "Mira")
            .Add("location", "moonlit shrine")
            .BuildCanonicalJson();

        Assert.Equal(a, b);
        Assert.Equal("{\"location\":\"moonlit shrine\",\"oracleMood\":\"pleased but ominous\",\"playerName\":\"Mira\"}", a);
    }

    [Fact]
    public void ContextBuilder_RejectsDuplicateKeys()
    {
        var builder = new LlmContextBuilder().Add("x", "1");
        Assert.Throws<InvalidOperationException>(() => builder.Add("x", "2"));
    }

    [Fact]
    public void ContextBuilder_RejectsEmptyKey()
    {
        var builder = new LlmContextBuilder();
        Assert.Throws<ArgumentException>(() => builder.Add(" ", "value"));
    }

    [Fact]
    public void RequestHasher_ReturnsSameHash_ForSameRequest()
    {
        var request = CreateRequest();
        var h1 = LlmRequestHasher.ComputeHash(request);
        var h2 = LlmRequestHasher.ComputeHash(request);
        Assert.Equal(h1, h2);
    }

    public static IEnumerable<object[]> HashMutationCases()
    {
        var request = CreateRequest();
        yield return [new Func<LlmTextRequest, LlmTextRequest>(r => CloneRequest(r, StableId: "story.oracle.line.02"))];
        yield return [new Func<LlmTextRequest, LlmTextRequest>(r => CloneRequest(r, Intent: "narrate.scene.alt"))];
        yield return [new Func<LlmTextRequest, LlmTextRequest>(r => CloneRequest(r, Persona: "narrator:lyrical"))];
        yield return [new Func<LlmTextRequest, LlmTextRequest>(r => CloneRequest(r, CanonicalContextJson: "{\"location\":\"storm tower\"}"))];
        yield return [new Func<LlmTextRequest, LlmTextRequest>(r => CloneRequest(r, Sampling: new LlmSamplingOptions(r.Sampling.Provider, r.Sampling.Model, Temperature: 0.4, MaxOutputTokens: r.Sampling.MaxOutputTokens, TopP: r.Sampling.TopP)))];
        yield return [new Func<LlmTextRequest, LlmTextRequest>(r => CloneRequest(r, PromptTemplateVersion: "llm.text.prompt.v2"))];
        yield return [new Func<LlmTextRequest, LlmTextRequest>(r => CloneRequest(r, OutputContractVersion: "llm.text.v2"))];
    }

    [Theory]
    [MemberData(nameof(HashMutationCases))]
    public void RequestHasher_ChangesHash_WhenMutableInputChanges(Func<LlmTextRequest, LlmTextRequest> mutate)
    {
        var original = CreateRequest();
        var changed = mutate(original);

        var originalHash = LlmRequestHasher.ComputeHash(original);
        var changedHash = LlmRequestHasher.ComputeHash(changed);

        Assert.NotEqual(originalHash, changedHash);
    }

    [Fact]
    public void RequestHasher_ChangesHash_WhenStableIdChanges() => AssertHashChanges(r => CloneRequest(r, StableId: "story.oracle.line.02"));

    [Fact]
    public void RequestHasher_ChangesHash_WhenIntentChanges() => AssertHashChanges(r => CloneRequest(r, Intent: "narrate.scene.alt"));

    [Fact]
    public void RequestHasher_ChangesHash_WhenPersonaChanges() => AssertHashChanges(r => CloneRequest(r, Persona: "narrator:lyrical"));

    [Fact]
    public void RequestHasher_ChangesHash_WhenContextChanges() => AssertHashChanges(r => CloneRequest(r, CanonicalContextJson: "{\"location\":\"storm tower\"}"));

    [Fact]
    public void RequestHasher_ChangesHash_WhenSamplingChanges() => AssertHashChanges(r => CloneRequest(r, Sampling: new LlmSamplingOptions(r.Sampling.Provider, r.Sampling.Model, Temperature: 0.4, MaxOutputTokens: r.Sampling.MaxOutputTokens, TopP: r.Sampling.TopP)));

    [Fact]
    public void RequestHasher_ChangesHash_WhenPromptTemplateVersionChanges() => AssertHashChanges(r => CloneRequest(r, PromptTemplateVersion: "llm.text.prompt.v2"));

    [Fact]
    public void RequestHasher_ChangesHash_WhenOutputContractVersionChanges() => AssertHashChanges(r => CloneRequest(r, OutputContractVersion: "llm.text.v2"));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void LlmSamplingOptions_RejectsEmptyProvider(string provider)
    {
        Assert.Throws<ArgumentException>(() => new LlmSamplingOptions(provider, "scripted-v1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void LlmSamplingOptions_RejectsEmptyModel(string model)
    {
        Assert.Throws<ArgumentException>(() => new LlmSamplingOptions("fake", model));
    }

    [Fact]
    public void LlmSamplingOptions_RejectsNegativeTemperature()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlmSamplingOptions("fake", "scripted-v1", Temperature: -0.001));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    [InlineData(1.1)]
    public void LlmSamplingOptions_RejectsInvalidTopP(double topP)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlmSamplingOptions("fake", "scripted-v1", TopP: topP));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void LlmSamplingOptions_RejectsNonPositiveMaxOutputTokens(int maxOutputTokens)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlmSamplingOptions("fake", "scripted-v1", MaxOutputTokens: maxOutputTokens));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void LlmTextRequest_RejectsEmptyStableId(string value)
    {
        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), StableId: value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void LlmTextRequest_RejectsEmptyIntent(string value)
    {
        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), Intent: value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void LlmTextRequest_RejectsEmptyPersona(string value)
    {
        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), Persona: value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void LlmTextRequest_RejectsEmptyCanonicalContext(string value)
    {
        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), CanonicalContextJson: value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void LlmTextRequest_RejectsEmptyPromptTemplateVersion(string value)
    {
        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), PromptTemplateVersion: value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void LlmTextRequest_RejectsEmptyOutputContractVersion(string value)
    {
        Assert.Throws<ArgumentException>(() => CloneRequest(CreateRequest(), OutputContractVersion: value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void LlmTextResult_RejectsEmptyRequestHash(string value)
    {
        Assert.Throws<ArgumentException>(() => new LlmTextResult("ok", value));
    }

    [Fact]
    public void LlmTextResult_RejectsNegativeInputTokenCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlmTextResult("ok", "abc", InputTokens: -1));
    }

    [Fact]
    public void LlmTextResult_RejectsNegativeOutputTokenCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlmTextResult("ok", "abc", OutputTokens: -1));
    }

    [Fact]
    public async Task FakeLlmClient_ReturnsConfiguredText()
    {
        var client = new FakeLlmClient("hello from fake");
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        var result = await client.GenerateTextAsync(request, hash, CancellationToken.None);

        Assert.Equal("hello from fake", result.Text);
        Assert.Equal(hash, result.RequestHash);
        Assert.Equal("fake", result.Provider);
        Assert.Equal("scripted-v1", result.Model);
    }

    [Fact]
    public async Task FakeLlmClient_IncrementsCallCount()
    {
        var client = new FakeLlmClient("x");
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        await client.GenerateTextAsync(request, hash, CancellationToken.None);
        await client.GenerateTextAsync(request, hash, CancellationToken.None);

        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task FakeLlmClient_RecordsLastRequestAndHash()
    {
        var client = new FakeLlmClient("x");
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        await client.GenerateTextAsync(request, hash, CancellationToken.None);

        Assert.Same(request, client.LastRequest);
        Assert.Equal(hash, client.LastRequestHash);
    }

    [Fact]
    public async Task FakeLlmClient_HonorsCancellation()
    {
        var client = new FakeLlmClient("x");
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GenerateTextAsync(request, hash, cts.Token));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void InMemoryCassette_ReturnsStoredEntry()
    {
        var cassette = new InMemoryLlmCassette();
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var result = new LlmTextResult("oracle line", hash, Provider: "fake", Model: "scripted-v1");

        cassette.Put(hash, request, result);

        Assert.True(cassette.TryGet(hash, out var stored));
        Assert.Equal(result, stored);
    }

    [Fact]
    public void InMemoryCassette_ReturnsFalseForMissingHash()
    {
        var cassette = new InMemoryLlmCassette();
        Assert.False(cassette.TryGet("missing", out _));
    }

    [Fact]
    public void InMemoryCassette_AllowsIdempotentPut()
    {
        var cassette = new InMemoryLlmCassette();
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        cassette.Put(hash, request, new LlmTextResult("oracle line", hash));
        cassette.Put(hash, request, new LlmTextResult("oracle line", hash));

        Assert.True(cassette.TryGet(hash, out var stored));
        Assert.Equal("oracle line", stored.Text);
    }

    [Fact]
    public void InMemoryCassette_RejectsSameHashWithDifferentText()
    {
        var cassette = new InMemoryLlmCassette();
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        cassette.Put(hash, request, new LlmTextResult("oracle line", hash));

        Assert.Throws<InvalidOperationException>(() => cassette.Put(hash, request, new LlmTextResult("different", hash)));
    }

    private static LlmTextRequest CreateRequest() => new(
        StableId: "story.oracle.line.01",
        Intent: "narrate.scene",
        Persona: "narrator:oracle",
        CanonicalContextJson: "{\"location\":\"moonlit shrine\",\"playerName\":\"Mira\"}",
        Sampling: new LlmSamplingOptions("fake", "scripted-v1", Temperature: 0.0, MaxOutputTokens: 128, TopP: 1.0),
        PromptTemplateVersion: LlmTextRequest.DefaultPromptTemplateVersion,
        OutputContractVersion: LlmTextRequest.DefaultOutputContractVersion);


    private static LlmTextRequest CloneRequest(
        LlmTextRequest source,
        string? StableId = null,
        string? Intent = null,
        string? Persona = null,
        string? CanonicalContextJson = null,
        LlmSamplingOptions? Sampling = null,
        string? PromptTemplateVersion = null,
        string? OutputContractVersion = null) => new(
            StableId: StableId ?? source.StableId,
            Intent: Intent ?? source.Intent,
            Persona: Persona ?? source.Persona,
            CanonicalContextJson: CanonicalContextJson ?? source.CanonicalContextJson,
            Sampling: Sampling ?? source.Sampling,
            PromptTemplateVersion: PromptTemplateVersion ?? source.PromptTemplateVersion,
            OutputContractVersion: OutputContractVersion ?? source.OutputContractVersion);

    private static void AssertHashChanges(Func<LlmTextRequest, LlmTextRequest> mutator)
    {
        var request = CreateRequest();
        var changed = mutator(request);
        Assert.NotEqual(LlmRequestHasher.ComputeHash(request), LlmRequestHasher.ComputeHash(changed));
    }
}
