using System.Text.Json;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class NativeConversationTests
{
    private sealed class Transport(string reply, int status = 200) : ILlmHttpTransport
    {
        public List<LlmHttpRequest> Requests { get; } = [];
        public Task<LlmHttpResponse> SendAsync(LlmHttpRequest request, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Requests.Add(request); return Task.FromResult(new LlmHttpResponse(status, reply)); }
    }
    private static JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
    private static LlmConversationRequest Request() => new("Engineering assistant", [Json("""{"role":"user","content":"Inspect"}""")],
        [Json("""{"type":"function","name":"inspect","description":"Inspect source","parameters":{"type":"object","properties":{},"additionalProperties":false}}""")]);
    private const string Claude = """{"id":"r1","stop_reason":"tool_use","content":[{"type":"thinking","thinking":"private thought","signature":"opaque-claude"},{"type":"tool_use","id":"c1","name":"inspect","input":{}},{"type":"tool_use","id":"c2","name":"inspect","input":{}}],"usage":{"input_tokens":10,"output_tokens":7,"cache_read_input_tokens":3}}""";
    private const string Gemini = """{"responseId":"r1","candidates":[{"finishReason":"STOP","content":{"role":"model","parts":[{"text":"private thought","thought":true},{"functionCall":{"id":"c1","name":"inspect","args":{}},"thoughtSignature":"opaque-gemini"},{"functionCall":{"name":"inspect","args":{}}}]}}],"usageMetadata":{"promptTokenCount":13,"candidatesTokenCount":4,"thoughtsTokenCount":3}}""";
    private static string Reply(bool gemini) => gemini ? Gemini : Claude;
    private static ILlmConversationClient Client(bool gemini, Transport transport, string? model = null) => gemini
        ? new GeminiConversationClient(new("gemini", model ?? "gemini-4-argon", new("https://generativelanguage.googleapis.com/v1beta/models/gemini-4-argon:generateContent"), "secret-fixture"), transport)
        : new AnthropicConversationClient(new("anthropic", model ?? "claude-opus-5-5", new("https://api.anthropic.com/v1/messages"), "secret-fixture"), transport);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Replays_native_signatures_parallel_calls_and_success_failure_results(bool gemini)
    {
        var transport = new Transport(Reply(gemini)); var client = Client(gemini, transport); var request = Request();
        var first = await client.RespondAsync(request, CancellationToken.None);
        Assert.Equal(2, first.Calls.Count); Assert.Empty(first.Text);
        Assert.Equal(13, first.InputTokens); Assert.Equal(7, first.OutputTokens);
        Assert.Equal("c1", first.Calls[0].CallId); Assert.NotEqual(first.Calls[0].CallId, first.Calls[1].CallId);
        var input = request.Input.Concat(first.Output).Concat(first.Calls.Select((call, index) => JsonSerializer.SerializeToElement(new {
            type = "function_call_output", call_id = call.CallId, output = index == 0 ? "{\"ok\":true,\"result\":{}}" : "{\"ok\":false,\"error\":\"Missing file\"}"
        }))).ToArray();
        await client.RespondAsync(request with { Input = input }, CancellationToken.None);
        var sent = Json(transport.Requests[1].Body); var messages = sent.GetProperty(gemini ? "contents" : "messages");
        Assert.Equal(3, messages.GetArrayLength());
        Assert.Equal(first.Output[0].GetProperty("message").GetRawText(), messages[1].GetRawText());
        var results = messages[2].GetProperty(gemini ? "parts" : "content"); Assert.Equal(2, results.GetArrayLength());
        if (gemini)
        {
            Assert.Equal("medium", sent.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
            Assert.Equal("opaque-gemini", messages[1].GetProperty("parts")[1].GetProperty("thoughtSignature").GetString());
            Assert.Equal("c1", results[0].GetProperty("functionResponse").GetProperty("id").GetString());
            Assert.False(results[1].GetProperty("functionResponse").TryGetProperty("id", out _));
            Assert.False(results[1].GetProperty("functionResponse").GetProperty("response").GetProperty("ok").GetBoolean());
            Assert.Equal("object", sent.GetProperty("tools")[0].GetProperty("functionDeclarations")[0].GetProperty("parametersJsonSchema").GetProperty("type").GetString());
            Assert.Equal("secret-fixture", transport.Requests[0].Headers["x-goog-api-key"]);
        }
        else
        {
            Assert.Equal("medium", sent.GetProperty("output_config").GetProperty("effort").GetString());
            Assert.Equal("opaque-claude", messages[1].GetProperty("content")[0].GetProperty("signature").GetString());
            Assert.Equal("c2", results[1].GetProperty("tool_use_id").GetString()); Assert.True(results[1].GetProperty("is_error").GetBoolean());
            Assert.Equal("object", sent.GetProperty("tools")[0].GetProperty("input_schema").GetProperty("type").GetString());
            Assert.Equal("secret-fixture", transport.Requests[0].Headers["x-api-key"]);
            Assert.Equal("2023-06-01", transport.Requests[0].Headers["anthropic-version"]);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Rejects_missing_results_cross_model_and_cross_provider_history_before_transport(bool gemini)
    {
        var transport = new Transport(Reply(gemini)); var request = Request();
        var turn = await Client(gemini, transport).RespondAsync(request, CancellationToken.None);
        var next = request with { Input = request.Input.Concat(turn.Output).ToArray() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(gemini, transport).RespondAsync(next, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(gemini, transport, "another-model").RespondAsync(next, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(!gemini, transport).RespondAsync(next, CancellationToken.None));
        Assert.Single(transport.Requests);
    }

    [Theory]
    [InlineData(false, "truncated")] [InlineData(true, "truncated")]
    [InlineData(false, "unknown")] [InlineData(true, "unknown")]
    [InlineData(false, "arguments")] [InlineData(true, "arguments")]
    [InlineData(false, "duplicate")] [InlineData(true, "duplicate")]
    public async Task Rejected_responses_never_become_host_effects(bool gemini, string defect)
    {
        var reply = defect switch {
            "truncated" => Reply(gemini).Replace(gemini ? "STOP" : "tool_use\",\"content", gemini ? "MAX_TOKENS" : "max_tokens\",\"content"),
            "unknown" => Reply(gemini).Replace("inspect", "shell"),
            "arguments" => Reply(gemini).Replace(gemini ? "\"args\":{}" : "\"input\":{}", gemini ? "\"args\":[]" : "\"input\":[]"),
            _ => gemini ? Gemini.Replace("{\"name\":\"inspect\",\"args\":{}}", "{\"id\":\"c1\",\"name\":\"inspect\",\"args\":{}}") : Claude.Replace("c2", "c1")
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(gemini, new(reply)).RespondAsync(Request(), CancellationToken.None));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Final_text_http_errors_and_cancellation_are_explicit(bool gemini)
    {
        var reply = gemini ? """{"candidates":[{"finishReason":"STOP","content":{"role":"model","parts":[{"text":"Done"}]}}],"usageMetadata":{}}"""
            : """{"id":"r2","stop_reason":"end_turn","content":[{"type":"text","text":"Done"}],"usage":{}}""";
        var turn = await Client(gemini, new(reply)).RespondAsync(Request(), CancellationToken.None);
        Assert.Equal("Done", turn.Text); Assert.Empty(turn.Calls);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(gemini, new("secret-fixture private-source", 401)).RespondAsync(Request(), CancellationToken.None));
        Assert.DoesNotContain("secret-fixture", error.Message); Assert.DoesNotContain("private-source", error.Message);
        var transport = new Transport(reply); using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(gemini, transport).RespondAsync(Request(), ct.Token));
        Assert.Empty(transport.Requests);
    }
}
