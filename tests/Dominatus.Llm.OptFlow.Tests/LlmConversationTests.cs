using System.Text.Json;
using Xunit;

namespace Dominatus.Llm.OptFlow.Tests;

public sealed class LlmConversationTests
{
    private sealed class WireHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
    [Fact]
    public async Task Http_transport_replaces_StringContent_default_with_the_declared_content_type()
    {
        using var client = new HttpClient(new WireHandler());
        var transport = new HttpClientLlmTransport(client);
        var response = await transport.SendAsync(new(HttpMethod.Post, new("https://api.openai.com/v1/responses"),
            new Dictionary<string, string> { ["Content-Type"] = "application/json" }, "{}"), CancellationToken.None);
        Assert.Equal(200, response.StatusCode);
    }
    private sealed class Transport(string reply, int status = 200) : ILlmHttpTransport
    {
        public LlmHttpRequest? Request { get; private set; }
        public Task<LlmHttpResponse> SendAsync(LlmHttpRequest request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Request = request; return Task.FromResult(new LlmHttpResponse(status, reply)); }
    }
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private static LlmConversationRequest Request() => new("Engineering assistant", [Json("{\"role\":\"user\",\"content\":\"Inspect\"}")],
        [Json("{\"type\":\"function\",\"name\":\"inspect\",\"parameters\":{\"type\":\"object\"}}")]);
    private static OpenAiConversationClient Client(Transport transport, string model = "gpt-6.1-sol")
        => new(new("openai", model, new Uri("https://api.openai.com/v1/responses"), "test-secret"), transport);

    [Theory]
    [InlineData("gpt-6-astra")]
    [InlineData("gpt-6.1-sol")]
    [InlineData("gpt-6-luna")]
    public async Task Preserves_reasoning_and_tool_calls_without_executing_them(string model)
    {
        var transport = new Transport("""{"id":"r1","status":"completed","output":[{"type":"reasoning","encrypted_content":"opaque","summary":[]},{"type":"function_call","call_id":"c1","name":"inspect","arguments":"{}"}],"usage":{"input_tokens":12,"output_tokens":6}}""");
        var result = await Client(transport, model).RespondAsync(Request(), CancellationToken.None);
        Assert.Equal("opaque", result.Output[0].GetProperty("encrypted_content").GetString());
        Assert.Equal("c1", Assert.Single(result.Calls).CallId);
        Assert.Equal(12, result.InputTokens);
        var sent = Json(transport.Request!.Body);
        Assert.Equal(model, sent.GetProperty("model").GetString());
        Assert.False(sent.GetProperty("store").GetBoolean());
        Assert.Contains("reasoning.encrypted_content", sent.GetProperty("include").EnumerateArray().Select(x => x.GetString()));
    }

    [Theory]
    [InlineData("incomplete", "inspect")]
    [InlineData("completed", "unregistered")]
    public async Task Rejects_incomplete_or_unregistered_actions(string status, string name)
    {
        var response = $$"""{"id":"r","status":"{{status}}","output":[{"type":"function_call","call_id":"c","name":"{{name}}","arguments":"{}"}]}""";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(new(response)).RespondAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task Provider_failures_do_not_disclose_credentials_or_payloads()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Client(new("test-secret private-source", 401)).RespondAsync(Request(), CancellationToken.None));
        Assert.DoesNotContain("test-secret", error.Message); Assert.DoesNotContain("private-source", error.Message);
    }

    [Theory]
    [InlineData("[{\"type\":\"reasoning\",\"summary\":[]}]")]
    [InlineData("[{\"type\":\"function_call\",\"call_id\":\"c\",\"name\":\"inspect\",\"arguments\":\"[]\"}]")]
    [InlineData("[{\"type\":\"function_call\",\"call_id\":\"c\",\"name\":\"inspect\",\"arguments\":\"{}\"},{\"type\":\"function_call\",\"call_id\":\"c\",\"name\":\"inspect\",\"arguments\":\"{}\"}]")]
    public async Task Empty_turns_invalid_arguments_and_duplicate_identities_never_become_effects(string output)
    {
        var response = "{\"id\":\"r\",\"status\":\"completed\",\"output\":" + output + "}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(new(response)).RespondAsync(Request(), CancellationToken.None));
    }
}
