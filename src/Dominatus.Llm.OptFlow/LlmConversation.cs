using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

/// <summary>Explicit conversation items, including opaque provider reasoning items.
/// The host owns the transcript and tool execution; a provider never executes tools.</summary>
public sealed record LlmConversationRequest(string Instructions, IReadOnlyList<JsonElement> Input,
    IReadOnlyList<JsonElement> Tools, string ReasoningEffort = "medium", int MaxOutputTokens = 8192);
public sealed record LlmFunctionCall(string CallId, string Name, string Arguments);
public sealed record LlmConversationTurn(string ResponseId, IReadOnlyList<JsonElement> Output,
    string Text, IReadOnlyList<LlmFunctionCall> Calls, int InputTokens, int OutputTokens);

public interface ILlmConversationClient
{
    Task<LlmConversationTurn> RespondAsync(LlmConversationRequest request, CancellationToken cancellationToken);
}

/// <summary>Responses tool protocol. Kept separate from the existing single-text-call contract.</summary>
public sealed class OpenAiConversationClient(LlmHttpProviderOptions options, ILlmHttpTransport transport)
    : LlmHttpTextClientBase(options, transport), ILlmConversationClient
{
    public async Task<LlmConversationTurn> RespondAsync(LlmConversationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxOutputTokens < 1) throw new ArgumentOutOfRangeException(nameof(request.MaxOutputTokens));
        var body = JsonSerializer.Serialize(new
        {
            model = Options.Model, instructions = request.Instructions, input = request.Input,
            tools = request.Tools, store = false, include = new[] { "reasoning.encrypted_content" },
            reasoning = new { effort = request.ReasoningEffort }, max_output_tokens = request.MaxOutputTokens
        });
        LlmHttpResponse response;
        try
        {
            response = await Transport.SendAsync(new(HttpMethod.Post, Options.Endpoint,
                new Dictionary<string, string> { ["Authorization"] = "Bearer " + Options.ApiKey,
                    ["Content-Type"] = "application/json" }, body), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // Transport implementations may include headers or payloads in exception text.
            throw new InvalidOperationException("OpenAI conversation transport failed.");
        }
        if (response.StatusCode is < 200 or >= 300)
            throw new InvalidOperationException($"OpenAI conversation request failed (HTTP {response.StatusCode}). Check server credentials, model access and quota.");
        using var parsed = JsonDocument.Parse(response.Body);
        var root = parsed.RootElement;
        if (!root.TryGetProperty("status", out var status) || status.GetString() != "completed")
            throw new InvalidOperationException("OpenAI conversation did not complete. No tool actions were accepted from the incomplete response.");
        var output = root.GetProperty("output").EnumerateArray().Select(x => x.Clone()).ToArray();
        var tools = request.Tools.Select(t => t.GetProperty("name").GetString()).ToHashSet(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var calls = new List<LlmFunctionCall>(); var text = new List<string>();
        foreach (var item in output)
        {
            var type = item.GetProperty("type").GetString();
            if (type == "function_call")
            {
                var id = item.GetProperty("call_id").GetString()!;
                var name = item.GetProperty("name").GetString()!;
                var arguments = item.GetProperty("arguments").GetString()!;
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id) || !tools.Contains(name))
                    throw new InvalidOperationException("OpenAI returned an unknown tool or duplicate tool call identity.");
                using var args = JsonDocument.Parse(arguments);
                if (args.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Tool arguments must be a JSON object.");
                calls.Add(new(id, name, arguments));
            }
            else if (type == "message")
            {
                foreach (var part in item.GetProperty("content").EnumerateArray())
                {
                    if (part.GetProperty("type").GetString() == "output_text") text.Add(part.GetProperty("text").GetString()!);
                    else if (part.GetProperty("type").GetString() == "refusal") text.Add(part.GetProperty("refusal").GetString()!);
                }
            }
            else if (type != "reasoning") throw new InvalidOperationException($"Unsupported OpenAI conversation item '{type}'.");
        }
        if (text.Count == 0 && calls.Count == 0)
            throw new InvalidOperationException("OpenAI returned no assistant text or function requests.");
        var usage = root.TryGetProperty("usage", out var u) ? u : default;
        int Tokens(string name) => usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(name, out var n) ? n.GetInt32() : 0;
        return new(root.GetProperty("id").GetString()!, output, string.Join("\n", text), calls, Tokens("input_tokens"), Tokens("output_tokens"));
    }
}
