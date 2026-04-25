using System.Text;
using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

public sealed class OpenAiResponsesDecisionClient : LlmHttpTextClientBase, ILlmDecisionClient
{
    private const string UserAgent = "Dominatus.Llm.OptFlow/openai-responses-decision-v1";
    private const string DecisionInstructions = "You are scoring a closed option set for a deterministic runtime. Return only strict JSON matching the schema.";
    private const int DefaultMaxOutputTokens = 512;

    public OpenAiResponsesDecisionClient(LlmHttpProviderOptions options, ILlmHttpTransport transport)
        : base(options, transport)
    {
    }

    public async Task<LlmDecisionResult> ScoreOptionsAsync(LlmDecisionRequest request, string requestHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);

        cancellationToken.ThrowIfCancellationRequested();

        var context = BuildContext(request.StableId, requestHash);
        var httpRequest = new LlmHttpRequest(
            Method: HttpMethod.Post,
            Uri: Options.Endpoint,
            Headers: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Authorization"] = $"Bearer {Options.ApiKey}",
                ["Content-Type"] = "application/json",
                ["Accept"] = "application/json",
                ["User-Agent"] = UserAgent,
            },
            Body: BuildRequestBody(request, Options.Model));

        LlmHttpResponse response;
        try
        {
            response = await Transport.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Transport failure ({context}). {Redact(ex.Message, Options.ApiKey)}", ex);
        }

        if (response.StatusCode is < 200 or >= 300)
        {
            throw BuildNonSuccessStatusException(response, context);
        }

        if (string.IsNullOrWhiteSpace(response.Body))
        {
            throw new InvalidOperationException($"HTTP response body was empty ({context}).");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(response.Body);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Malformed HTTP response JSON ({context}). {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("output", out var outputElement) || outputElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"HTTP response JSON missing required output array ({context}).");
            }

            var text = AggregateOutputText(outputElement, context);
            return LlmDecisionJsonParser.ParseAndValidate(text, request, requestHash, context);
        }
    }

    private InvalidOperationException BuildNonSuccessStatusException(LlmHttpResponse response, string context)
    {
        var detail = ParseOpenAiError(response.Body);
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
        return new InvalidOperationException($"HTTP request failed with status {response.StatusCode} ({context}).{suffix}");
    }

    private string ParseOpenAiError(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "Response body was empty.";
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (!root.TryGetProperty("error", out var errorElement) || errorElement.ValueKind != JsonValueKind.Object)
            {
                return "Response body did not contain an OpenAI error object.";
            }

            var type = errorElement.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            var code = errorElement.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
            var message = errorElement.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
            return $"OpenAI error type='{type ?? "unknown"}', code='{code ?? "unknown"}', message='{Redact(message ?? string.Empty, Options.ApiKey)}'.";
        }
        catch (JsonException)
        {
            return "Response body was malformed JSON.";
        }
    }

    private static string AggregateOutputText(JsonElement outputElement, string context)
    {
        var sb = new StringBuilder();
        var hasOutput = false;

        foreach (var outputItem in outputElement.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var contentElement) || contentElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var block in contentElement.EnumerateArray())
            {
                var blockType = block.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                if (!string.Equals(blockType, "output_text", StringComparison.Ordinal))
                {
                    continue;
                }

                hasOutput = true;
                var text = block.TryGetProperty("text", out var textElement) ? textElement.GetString() : null;
                if (!string.IsNullOrEmpty(text))
                {
                    sb.Append(text);
                }
            }
        }

        if (!hasOutput)
        {
            throw new InvalidOperationException($"HTTP response JSON contained no output_text blocks ({context}).");
        }

        var textOut = sb.ToString();
        if (string.IsNullOrWhiteSpace(textOut))
        {
            throw new InvalidOperationException($"HTTP response JSON contained empty output text ({context}).");
        }

        return textOut;
    }

    private static string BuildRequestBody(LlmDecisionRequest request, string model)
    {
        var prompt = LlmDecisionPromptBuilder.BuildPrompt(request);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);
            writer.WriteString("instructions", DecisionInstructions);

            writer.WritePropertyName("input");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WritePropertyName("content");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("type", "input_text");
            writer.WriteString("text", prompt);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteNumber("temperature", request.Sampling.Temperature);
            writer.WriteNumber("max_output_tokens", request.Sampling.MaxOutputTokens ?? DefaultMaxOutputTokens);
            if (request.Sampling.TopP is not null)
            {
                writer.WriteNumber("top_p", request.Sampling.TopP.Value);
            }

            writer.WriteBoolean("store", false);
            writer.WritePropertyName("tools");
            writer.WriteStartArray();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
