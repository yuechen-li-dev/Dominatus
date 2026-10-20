using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

/// <summary>Gemini generateContent tool turns; opaque thought signatures remain native.</summary>
public sealed class GeminiConversationClient(LlmHttpProviderOptions options, ILlmHttpTransport transport)
    : LlmHttpTextClientBase(options, transport), ILlmConversationClient
{
    public async Task<LlmConversationTurn> RespondAsync(LlmConversationRequest request, CancellationToken cancellationToken)
    {
        ConversationProtocol.Validate(request);
        var body = new { systemInstruction = new { parts = new[] { new { text = request.Instructions } } },
            contents = ConversationProtocol.History("gemini", Options.Model, request.Input),
            tools = new[] { new { functionDeclarations = request.Tools.Select(t => new { name = t.GetProperty("name"),
                description = t.TryGetProperty("description", out var d) ? d.GetString() : "", parametersJsonSchema = t.GetProperty("parameters") }).ToArray() } },
            generationConfig = new { maxOutputTokens = request.MaxOutputTokens, candidateCount = 1,
                thinkingConfig = new { thinkingLevel = request.ReasoningEffort } } };
        using var parsed = await ConversationProtocol.Send(Options, Transport, body,
            new() { ["x-goog-api-key"] = Options.ApiKey }, cancellationToken).ConfigureAwait(false);
        var root = parsed.RootElement;
        if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out _))
            throw new InvalidOperationException("Gemini blocked this request. No tool actions were accepted.");
        var candidates = root.GetProperty("candidates");
        if (candidates.GetArrayLength() != 1 || candidates[0].GetProperty("finishReason").GetString() != "STOP")
            throw new InvalidOperationException("Gemini did not complete its turn. No tool actions were accepted.");
        var content = candidates[0].GetProperty("content");
        if (content.GetProperty("role").GetString() != "model") throw new InvalidOperationException("Invalid Gemini response role.");
        var calls = new List<LlmFunctionCall>(); var text = new List<string>();
        foreach (var part in content.GetProperty("parts").EnumerateArray())
        {
            if (part.TryGetProperty("functionCall", out var call))
                ConversationProtocol.AddCall(calls, request, call.TryGetProperty("id", out var id) ? id.GetString()! : Guid.NewGuid().ToString("N"),
                    call.GetProperty("name").GetString()!, call.GetProperty("args"));
            else if (part.TryGetProperty("text", out var value))
            { if (!part.TryGetProperty("thought", out var thought) || !thought.GetBoolean()) text.Add(value.GetString()!); }
            else throw new InvalidOperationException("Unsupported Gemini conversation content.");
        }
        if (text.Count == 0 && calls.Count == 0) throw new InvalidOperationException("Gemini returned no assistant text or function requests.");
        var usage = root.GetProperty("usageMetadata");
        int Tokens(string name) => usage.TryGetProperty(name, out var n) ? n.GetInt32() : 0;
        return new(root.TryGetProperty("responseId", out var responseId) ? responseId.GetString()! : "",
            [ConversationProtocol.Envelope("gemini", Options.Model, content, calls)], string.Join("\n", text), calls,
            Tokens("promptTokenCount"), Tokens("candidatesTokenCount") + Tokens("thoughtsTokenCount"));
    }
}
