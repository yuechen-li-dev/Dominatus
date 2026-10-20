using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dominatus.Llm.OptFlow;

internal static class ConversationProtocol
{
    public static async Task<JsonDocument> Send(LlmHttpProviderOptions options, ILlmHttpTransport transport,
        object body, Dictionary<string, string> headers, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        headers["Content-Type"] = "application/json";
        LlmHttpResponse response;
        try { response = await transport.SendAsync(new(HttpMethod.Post, options.Endpoint, headers, JsonSerializer.Serialize(body)), ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new InvalidOperationException($"{options.Provider} conversation transport failed."); }
        if (response.StatusCode is < 200 or >= 300)
            throw new InvalidOperationException($"{options.Provider} conversation failed (HTTP {response.StatusCode}). Check server credentials, model access and quota.");
        try { return JsonDocument.Parse(response.Body); }
        catch (JsonException) { throw new InvalidOperationException($"{options.Provider} returned malformed conversation JSON."); }
    }

    public static void Validate(LlmConversationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxOutputTokens < 1) throw new ArgumentOutOfRangeException(nameof(request.MaxOutputTokens));
        if (request.ReasoningEffort is not ("low" or "medium" or "high"))
            throw new ArgumentException("This conversation adapter admits low, medium or high reasoning effort.", nameof(request));
    }

    public static void AddCall(List<LlmFunctionCall> calls, LlmConversationRequest request, string id, string name, JsonElement arguments)
    {
        if (string.IsNullOrWhiteSpace(id) || calls.Any(c => c.CallId == id)
            || !request.Tools.Any(t => t.GetProperty("name").GetString() == name))
            throw new InvalidOperationException("Provider returned an unknown tool or duplicate tool call identity.");
        if (arguments.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Tool arguments must be a JSON object.");
        calls.Add(new(id, name, arguments.GetRawText()));
    }

    public static JsonElement Envelope(string provider, string model, JsonElement message, List<LlmFunctionCall> calls)
        => JsonSerializer.SerializeToElement(new { type = "provider_message", provider, model, message, callIds = calls.Select(c => c.CallId).ToArray() });

    // Preserve whole native messages (including signatures). Only user text and tool results
    // are translated; provider/model changes must start a fresh transcript.
    public static List<JsonObject> History(string provider, string model, IReadOnlyList<JsonElement> input)
    {
        var gemini = provider == "gemini";
        var messages = new List<JsonObject>();
        var pending = new Dictionary<string, (string Name, string? NativeId)>(StringComparer.Ordinal);
        JsonArray? results = null;
        string contentKey = gemini ? "parts" : "content";
        foreach (var item in input)
        {
            var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type == "function_call_output")
            {
                if (!pending.Remove(item.GetProperty("call_id").GetString()!, out var call))
                    throw new InvalidOperationException("Tool result has no pending call in this conversation.");
                if (results is null) { results = []; messages.Add(new() { ["role"] = "user", [contentKey] = results }); }
                var output = item.GetProperty("output").GetString()!;
                using var parsed = JsonDocument.Parse(output);
                if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Tool result must be a JSON object.");
                if (gemini)
                {
                    var response = new JsonObject { ["name"] = call.Name, ["response"] = JsonNode.Parse(output) };
                    if (call.NativeId is not null) response["id"] = call.NativeId;
                    results.Add(new JsonObject { ["functionResponse"] = response });
                }
                else results.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = call.NativeId, ["content"] = output,
                    ["is_error"] = parsed.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False });
                continue;
            }
            if (pending.Count != 0) throw new InvalidOperationException("Complete all tool results before continuing the conversation.");
            results = null;
            if (type == "provider_message")
            {
                if (item.GetProperty("provider").GetString() != provider || item.GetProperty("model").GetString() != model)
                    throw new InvalidOperationException("Start a new conversation when changing provider or model.");
                var message = item.GetProperty("message");
                if (message.GetProperty("role").GetString() != (gemini ? "model" : "assistant"))
                    throw new InvalidOperationException("Invalid provider message role.");
                messages.Add(JsonNode.Parse(message.GetRawText())!.AsObject());
                var callIds = item.GetProperty("callIds").EnumerateArray().Select(c => c.GetString()!).ToArray();
                var index = 0;
                foreach (var part in message.GetProperty(contentKey).EnumerateArray())
                {
                    JsonElement call;
                    if (gemini) { if (!part.TryGetProperty("functionCall", out call)) continue; }
                    else { if (part.GetProperty("type").GetString() != "tool_use") continue; call = part; }
                    if (index >= callIds.Length || !pending.TryAdd(callIds[index++],
                        (call.GetProperty("name").GetString()!, call.TryGetProperty("id", out var id) ? id.GetString() : null)))
                        throw new InvalidOperationException("Invalid conversation tool identities.");
                }
                if (index != callIds.Length) throw new InvalidOperationException("Invalid conversation tool identities.");
            }
            else if (type is null && item.GetProperty("role").GetString() == "user" && item.GetProperty("content").ValueKind == JsonValueKind.String)
            {
                var text = item.GetProperty("content").GetString();
                messages.Add(new() { ["role"] = "user", [contentKey] = new JsonArray(gemini
                    ? new JsonObject { ["text"] = text } : new JsonObject { ["type"] = "text", ["text"] = text }) });
            }
            else throw new InvalidOperationException("Unsupported conversation item. Start a new conversation for another provider.");
        }
        if (pending.Count != 0) throw new InvalidOperationException("Missing conversation tool results.");
        return messages;
    }
}
