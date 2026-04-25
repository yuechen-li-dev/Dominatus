using System.Text;
using System.Text.Json;
using System.Threading;
using Dominatus.Core;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class JsonLlmCassetteTests
{
    [Fact]
    public void JsonCassette_RoundTripsEntry()
    {
        var path = CreateTempFilePath();
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var expected = new LlmTextResult("roundtrip", hash, Provider: "fake", Model: "scripted-v1", FinishReason: "stop", InputTokens: 12, OutputTokens: 7);

        var cassette = JsonLlmCassette.LoadOrCreate(path);
        cassette.Put(hash, request, expected);
        cassette.Save();

        var loaded = JsonLlmCassette.LoadOrCreate(path);
        Assert.True(loaded.TryGet(hash, out var actual));
        Assert.Equal(expected.Text, actual.Text);
        Assert.Equal(expected.Provider, actual.Provider);
        Assert.Equal(expected.Model, actual.Model);
        Assert.Equal(expected.FinishReason, actual.FinishReason);
        Assert.Equal(expected.InputTokens, actual.InputTokens);
        Assert.Equal(expected.OutputTokens, actual.OutputTokens);

        var fileText = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("\"stableId\": \"demo.oracle.greeting.v1\"", fileText, StringComparison.Ordinal);
        Assert.Contains("\"canonicalContextJson\": \"{", fileText, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonCassette_SavesEntriesInDeterministicOrder()
    {
        var path = CreateTempFilePath();
        var cassette = JsonLlmCassette.LoadOrCreate(path);

        var requestB = CreateRequest("demo.oracle.b.v1");
        var hashB = LlmRequestHasher.ComputeHash(requestB);
        var requestA = CreateRequest("demo.oracle.a.v1");
        var hashA = LlmRequestHasher.ComputeHash(requestA);

        cassette.Put(hashB, requestB, new LlmTextResult("b text", hashB));
        cassette.Put(hashA, requestA, new LlmTextResult("a text", hashA));
        cassette.Save();

        var first = File.ReadAllText(path, Encoding.UTF8);

        var firstHashIndex = first.IndexOf(hashA, StringComparison.Ordinal);
        var secondHashIndex = first.IndexOf(hashB, StringComparison.Ordinal);
        Assert.True(firstHashIndex >= 0);
        Assert.True(secondHashIndex >= 0);
        Assert.True(firstHashIndex < secondHashIndex);

        var loaded = JsonLlmCassette.LoadOrCreate(path);
        loaded.Save();

        var second = File.ReadAllText(path, Encoding.UTF8);
        Assert.Equal(first, second);
    }

    [Fact]
    public void JsonCassette_LoadOrCreate_MissingFileStartsEmpty()
    {
        var path = CreateTempFilePath();

        var cassette = JsonLlmCassette.LoadOrCreate(path);

        Assert.False(cassette.TryGet("missing", out _));
    }

    [Fact]
    public void JsonCassette_Save_CreatesParentDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "nested", "oracle.cassette.json");
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);

        var cassette = JsonLlmCassette.LoadOrCreate(path);
        cassette.Put(hash, request, new LlmTextResult("saved", hash));

        cassette.Save();

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void JsonCassette_AllowsIdempotentPut()
    {
        var path = CreateTempFilePath();
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var result = new LlmTextResult("same", hash);
        var cassette = JsonLlmCassette.LoadOrCreate(path);

        cassette.Put(hash, request, result);
        cassette.Put(hash, request, result);

        Assert.True(cassette.TryGet(hash, out var actual));
        Assert.Equal("same", actual.Text);
    }

    [Fact]
    public void JsonCassette_RejectsSameHashWithDifferentText()
    {
        var path = CreateTempFilePath();
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var cassette = JsonLlmCassette.LoadOrCreate(path);

        cassette.Put(hash, request, new LlmTextResult("first", hash));

        var ex = Assert.Throws<InvalidOperationException>(() => cassette.Put(hash, request, new LlmTextResult("second", hash)));
        Assert.Contains("different text payload", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonCassette_Load_MalformedJsonFails()
    {
        var path = CreateTempFilePath();
        WriteFixture(path, "{not valid json");

        var ex = Assert.Throws<InvalidOperationException>(() => JsonLlmCassette.LoadOrCreate(path));

        Assert.Contains("malformed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JsonCassette_Load_UnsupportedSchemaVersionFails()
    {
        var path = CreateTempFilePath();
        WriteFixture(path, """
            {
              "schemaVersion": "dom.llm.cassette.v999",
              "entries": []
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => JsonLlmCassette.LoadOrCreate(path));

        Assert.Contains("Unsupported cassette schemaVersion", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonCassette_Load_ResultHashMismatchFails()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var path = CreateTempFilePath();

        WriteFixture(path, BuildCassetteJson(hash, request, resultRequestHash: "different", text: "hello"));

        var ex = Assert.Throws<InvalidOperationException>(() => JsonLlmCassette.LoadOrCreate(path));

        Assert.Contains("result.requestHash must match", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonCassette_Load_RecomputedRequestHashMismatchFails()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var path = CreateTempFilePath();

        var mutatedRequest = request with { Intent = "mutated intent" };
        WriteFixture(path, BuildCassetteJson(hash, mutatedRequest, resultRequestHash: hash, text: "hello"));

        var ex = Assert.Throws<InvalidOperationException>(() => JsonLlmCassette.LoadOrCreate(path));

        Assert.Contains("requestHash mismatch", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonCassette_Load_DuplicateHashFails()
    {
        var request = CreateRequest();
        var hash = LlmRequestHasher.ComputeHash(request);
        var path = CreateTempFilePath();

        var entry = BuildEntryJson(hash, request, hash, "hello");
        WriteFixture(path, $$"""
            {
              "schemaVersion": "dom.llm.cassette.v1",
              "entries": [
                {{entry}},
                {{entry}}
              ]
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => JsonLlmCassette.LoadOrCreate(path));

        Assert.Contains("duplicate requestHash", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecordThenReplay_WithJsonCassette_SuppressesClientOnReplay()
    {
        var path = CreateTempFilePath();
        var request = CreateRequest();

        var recordCassette = JsonLlmCassette.LoadOrCreate(path);
        var recordClient = new StubLlmClient("recorded text");
        var recordCompletion = DispatchAndGetCompletion(new LlmTextActuationHandler(recordClient, recordCassette, LlmCassetteMode.Record), request);
        Assert.True(recordCompletion.Ok);
        recordCassette.Save();

        var replayCassette = JsonLlmCassette.LoadOrCreate(path);
        var replayClient = new StubLlmClient("different provider text");
        var replayCompletion = DispatchAndGetCompletion(new LlmTextActuationHandler(replayClient, replayCassette, LlmCassetteMode.Replay), request);

        Assert.True(replayCompletion.Ok);
        Assert.Equal("recorded text", replayCompletion.Payload);
        Assert.Equal(1, recordClient.CallCount);
        Assert.Equal(0, replayClient.CallCount);
    }


    private static void WriteFixture(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, Encoding.UTF8);
    }

    private static string CreateTempFilePath()
        => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "oracle.cassette.json");

    private static string BuildCassetteJson(string requestHash, LlmTextRequest request, string resultRequestHash, string text)
        => $$"""
            {
              "schemaVersion": "dom.llm.cassette.v1",
              "entries": [
                {{BuildEntryJson(requestHash, request, resultRequestHash, text)}}
              ]
            }
            """;

    private static string BuildEntryJson(string requestHash, LlmTextRequest request, string resultRequestHash, string text)
        => $$"""
            {
              "requestHash": "{{requestHash}}",
              "request": {
                "stableId": "{{request.StableId}}",
                "intent": "{{request.Intent}}",
                "persona": "{{request.Persona}}",
                "canonicalContextJson": {{JsonSerializer.Serialize(request.CanonicalContextJson)}},
                "sampling": {
                  "provider": "{{request.Sampling.Provider}}",
                  "model": "{{request.Sampling.Model}}",
                  "temperature": {{request.Sampling.Temperature}},
                  "maxOutputTokens": {{(request.Sampling.MaxOutputTokens is null ? "null" : request.Sampling.MaxOutputTokens.Value.ToString())}},
                  "topP": {{(request.Sampling.TopP is null ? "null" : request.Sampling.TopP.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}}
                },
                "promptTemplateVersion": "{{request.PromptTemplateVersion}}",
                "outputContractVersion": "{{request.OutputContractVersion}}"
              },
              "result": {
                "text": "{{text}}",
                "requestHash": "{{resultRequestHash}}",
                "provider": "fake",
                "model": "scripted-v1",
                "finishReason": null,
                "inputTokens": null,
                "outputTokens": null
              }
            }
            """;

    private static LlmTextRequest CreateRequest(string stableId = "demo.oracle.greeting.v1")
        => new(
            StableId: stableId,
            Intent: "greet the player at the shrine",
            Persona: "Ancient oracle. Warm, cryptic, concise.",
            CanonicalContextJson: "{\"location\":\"moonlit shrine\",\"oracleMood\":\"pleased but ominous\",\"playerName\":\"Mira\"}",
            Sampling: new LlmSamplingOptions("fake", "scripted-v1", Temperature: 0.0, MaxOutputTokens: null, TopP: null),
            PromptTemplateVersion: LlmTextRequest.DefaultPromptTemplateVersion,
            OutputContractVersion: LlmTextRequest.DefaultOutputContractVersion);

    private static ActuationCompleted DispatchAndGetCompletion(LlmTextActuationHandler handler, LlmTextRequest request)
    {
        var host = new ActuatorHost();
        host.Register(handler);

        var world = new AiWorld(host);
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = RootNode });

        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions()));
        world.Add(agent);

        var ctx = new AiCtx(world, agent, agent.Events, CancellationToken.None, world.View, world.Mail, world.Actuator);
        var dispatch = host.Dispatch(ctx, request);

        Assert.True(dispatch.Accepted);
        Assert.True(dispatch.Completed);

        var cursor = default(EventCursor);
        Assert.True(ctx.Agent.Events.TryConsume<ActuationCompleted>(ref cursor, null, out var completion));
        return completion;
    }

    private static IEnumerator<AiStep> RootNode(AiCtx _)
    {
        yield break;
    }

    private sealed class StubLlmClient : ILlmClient
    {
        private readonly string _text;

        public StubLlmClient(string text)
        {
            _text = text;
        }

        public int CallCount { get; private set; }

        public Task<LlmTextResult> GenerateTextAsync(LlmTextRequest request, string requestHash, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new LlmTextResult(_text, requestHash, Provider: "fake", Model: "scripted-v1"));
        }
    }
}
