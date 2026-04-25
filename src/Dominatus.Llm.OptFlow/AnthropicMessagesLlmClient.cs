using System.Text;
using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

public sealed class AnthropicMessagesLlmClient : LlmHttpTextClientBase, ILlmClient
{
    private const string UserAgent = "Dominatus.Llm.OptFlow/anthropic-messages-v1";
    private const string AnthropicVersion = "2023-06-01";
    private const string StatelessInstructions = "You are generating one bounded text response for a deterministic runtime. Follow the persona and intent. Return only the generated text.";
    private const int DefaultMaxOutputTokens = 64;

    public AnthropicMessagesLlmClient(LlmHttpProviderOptions options, ILlmHttpTransport transport)
        : base(options, transport)
    {
    }

    public async Task<LlmTextResult> GenerateTextAsync(LlmTextRequest request, string requestHash, CancellationToken cancellationToken)
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
                ["x-api-key"] = Options.ApiKey,
                ["anthropic-version"] = AnthropicVersion,
                ["Content-Type"] = "application/json",
                ["Accept"] = "application/json",
                ["User-Agent"] = UserAgent,
            },
            Body: BuildRequestBody(request, Options.Model));

        LlmHttpResponse httpResponse;
        try
        {
            httpResponse = await Transport.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Transport failure ({context}). {Sanitize(ex.Message)}", ex);
        }

        if (httpResponse.StatusCode is < 200 or >= 300)
        {
            throw BuildNonSuccessStatusException(httpResponse, context);
        }

        if (string.IsNullOrWhiteSpace(httpResponse.Body))
        {
            throw new InvalidOperationException($"HTTP response body was empty ({context}).");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(httpResponse.Body);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Malformed HTTP response JSON ({context}). {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;

            if (!root.TryGetProperty("content", out var contentElement) || contentElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"HTTP response JSON missing required content array ({context}).");
            }

            var text = AggregateText(contentElement, context);
            var (inputTokens, outputTokens) = ParseUsage(root, context);
            var stopReason = root.TryGetProperty("stop_reason", out var stopElement) ? stopElement.GetString() : null;

            return new LlmTextResult(
                Text: text,
                RequestHash: requestHash,
                Provider: Options.Provider,
                Model: Options.Model,
                FinishReason: string.IsNullOrWhiteSpace(stopReason) ? null : stopReason,
                InputTokens: inputTokens,
                OutputTokens: outputTokens);
        }
    }

    private static string BuildRequestBody(LlmTextRequest request, string model)
    {
        var inputText = BuildInputText(request);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);
            writer.WriteNumber("max_tokens", request.Sampling.MaxOutputTokens ?? DefaultMaxOutputTokens);
            writer.WriteNumber("temperature", request.Sampling.Temperature);
            writer.WriteNumber("top_p", request.Sampling.TopP ?? 1.0);
            writer.WriteString("system", StatelessInstructions);

            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteString("content", inputText);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildInputText(LlmTextRequest request)
        => $"StableId: {request.StableId}\nIntent: {request.Intent}\nPersona: {request.Persona}\nContextJson: {request.CanonicalContextJson}";

    private static string AggregateText(JsonElement contentElement, string context)
    {
        var builder = new StringBuilder();
        var hasTextBlock = false;

        foreach (var block in contentElement.EnumerateArray())
        {
            var blockType = block.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            if (!string.Equals(blockType, "text", StringComparison.Ordinal))
            {
                continue;
            }

            hasTextBlock = true;
            var text = block.TryGetProperty("text", out var textElement) ? textElement.GetString() : null;
            if (!string.IsNullOrEmpty(text))
            {
                builder.Append(text);
            }
        }

        if (!hasTextBlock)
        {
            throw new InvalidOperationException($"HTTP response JSON contained no text content blocks ({context}).");
        }

        var aggregate = builder.ToString();
        if (string.IsNullOrWhiteSpace(aggregate))
        {
            throw new InvalidOperationException($"HTTP response JSON contained empty text content ({context}).");
        }

        return aggregate;
    }

    private static (int? InputTokens, int? OutputTokens) ParseUsage(JsonElement root, string context)
    {
        if (!root.TryGetProperty("usage", out var usageElement) || usageElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return (null, null);
        }

        if (usageElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"HTTP response JSON usage was malformed ({context}).");
        }

        int? inputTokens = null;
        int? outputTokens = null;

        if (usageElement.TryGetProperty("input_tokens", out var inputElement) && inputElement.ValueKind != JsonValueKind.Null)
        {
            if (!inputElement.TryGetInt32(out var parsedInput))
            {
                throw new InvalidOperationException($"HTTP response JSON usage.input_tokens was malformed ({context}).");
            }

            if (parsedInput < 0)
            {
                throw new InvalidOperationException($"HTTP response JSON has invalid negative input_tokens ({context}).");
            }

            inputTokens = parsedInput;
        }

        if (usageElement.TryGetProperty("output_tokens", out var outputElement) && outputElement.ValueKind != JsonValueKind.Null)
        {
            if (!outputElement.TryGetInt32(out var parsedOutput))
            {
                throw new InvalidOperationException($"HTTP response JSON usage.output_tokens was malformed ({context}).");
            }

            if (parsedOutput < 0)
            {
                throw new InvalidOperationException($"HTTP response JSON has invalid negative output_tokens ({context}).");
            }

            outputTokens = parsedOutput;
        }

        return (inputTokens, outputTokens);
    }

    private InvalidOperationException BuildNonSuccessStatusException(LlmHttpResponse response, string context)
    {
        var detail = ParseAnthropicError(response.Body);
        var detailPart = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
        return new InvalidOperationException($"HTTP request failed with status {response.StatusCode} ({context}).{detailPart}");
    }

    private string ParseAnthropicError(string? responseBody)
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
                return "Response body did not contain an Anthropic error object.";
            }

            var errorType = errorElement.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            var errorMessage = errorElement.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
            return $"Anthropic error type='{errorType ?? "unknown"}', message='{Sanitize(errorMessage ?? string.Empty)}'.";
        }
        catch (JsonException)
        {
            return "Response body was malformed JSON.";
        }
    }

    private string Sanitize(string message)
    {
        var redacted = Redact(message, Options.ApiKey);
        redacted = redacted.Replace("x-api-key", "[REDACTED]", StringComparison.OrdinalIgnoreCase);
        redacted = redacted.Replace("x-goog-api-key", "[REDACTED]", StringComparison.OrdinalIgnoreCase);
        redacted = redacted.Replace("authorization", "[REDACTED]", StringComparison.OrdinalIgnoreCase);
        return redacted;
    }
}
