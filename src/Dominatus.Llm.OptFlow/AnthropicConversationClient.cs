using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

/// <summary>Claude Messages tool turns, with complete native thinking/signature preservation.</summary>
public sealed class AnthropicConversationClient(LlmHttpProviderOptions options, ILlmHttpTransport transport)
    : LlmHttpTextClientBase(options, transport), ILlmConversationClient
{
    public async Task<LlmConversationTurn> RespondAsync(LlmConversationRequest request, CancellationToken cancellationToken)
    {
        ConversationProtocol.Validate(request);
        var body = new { model = Options.Model, system = request.Instructions, max_tokens = request.MaxOutputTokens,
            thinking = new { type = "adaptive" }, output_config = new { effort = request.ReasoningEffort },
            messages = ConversationProtocol.History("anthropic", Options.Model, request.Input),
            tools = request.Tools.Select(t => new { name = t.GetProperty("name"), description = t.TryGetProperty("description", out var d) ? d.GetString() : "",
                input_schema = t.GetProperty("parameters") }).ToArray() };
        using var parsed = await ConversationProtocol.Send(Options, Transport, body,
            new() { ["x-api-key"] = Options.ApiKey, ["anthropic-version"] = "2023-06-01" }, cancellationToken).ConfigureAwait(false);
        var root = parsed.RootElement;
        var stop = root.GetProperty("stop_reason").GetString();
        if (stop is not ("end_turn" or "tool_use"))
            throw new InvalidOperationException("Claude did not complete its turn. No tool actions were accepted.");
        var content = root.GetProperty("content");
        var calls = new List<LlmFunctionCall>(); var text = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            switch (part.GetProperty("type").GetString())
            {
                case "text": text.Add(part.GetProperty("text").GetString()!); break;
                case "tool_use": ConversationProtocol.AddCall(calls, request, part.GetProperty("id").GetString()!, part.GetProperty("name").GetString()!, part.GetProperty("input")); break;
                case "thinking": case "redacted_thinking": break;
                default: throw new InvalidOperationException("Unsupported Claude conversation content.");
            }
        }
        if ((stop == "tool_use") != (calls.Count > 0) || text.Count == 0 && calls.Count == 0)
            throw new InvalidOperationException("Claude returned an inconsistent or empty turn.");
        var message = JsonSerializer.SerializeToElement(new { role = "assistant", content });
        var usage = root.GetProperty("usage");
        int Tokens(string name) => usage.TryGetProperty(name, out var n) ? n.GetInt32() : 0;
        return new(root.GetProperty("id").GetString()!, [ConversationProtocol.Envelope("anthropic", Options.Model, message, calls)],
            string.Join("\n", text), calls, Tokens("input_tokens") + Tokens("cache_creation_input_tokens") + Tokens("cache_read_input_tokens"), Tokens("output_tokens"));
    }
}
