using System.Text;
using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

public sealed class AnthropicMessagesDecisionClient : LlmHttpTextClientBase, ILlmDecisionClient
{
    private const string UserAgent = "Dominatus.Llm.OptFlow/anthropic-messages-decision-v1";
    private const string AnthropicVersion = "2023-06-01";
    private const string DecisionInstructions = "You are scoring a closed option set for a deterministic runtime. Return only strict JSON matching the schema.";
    private const int DefaultMaxOutputTokens = 512;

    public AnthropicMessagesDecisionClient(LlmHttpProviderOptions options, ILlmHttpTransport transport)
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
                ["x-api-key"] = Options.ApiKey,
                ["anthropic-version"] = AnthropicVersion,
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
            throw new InvalidOperationException($"Transport failure ({context}). {Sanitize(ex.Message)}", ex);
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
            if (!root.TryGetProperty("content", out var contentElement) || contentElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"HTTP response JSON missing required content array ({context}).");
            }

            var text = AggregateText(contentElement, context);
            return LlmDecisionJsonParser.ParseAndValidate(text, request, requestHash, context);
        }
    }

    private static string BuildRequestBody(LlmDecisionRequest request, string model)
    {
        var prompt = LlmDecisionPromptBuilder.BuildPrompt(request);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);
            writer.WriteNumber("max_tokens", request.Sampling.MaxOutputTokens ?? DefaultMaxOutputTokens);
            writer.WriteNumber("temperature", request.Sampling.Temperature);
            if (request.Sampling.TopP is not null)
            {
                writer.WriteNumber("top_p", request.Sampling.TopP.Value);
            }

            writer.WriteString("system", DecisionInstructions);
            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteString("content", prompt);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string AggregateText(JsonElement contentElement, string context)
    {
        var sb = new StringBuilder();
        var foundTextBlock = false;

        foreach (var block in contentElement.EnumerateArray())
        {
            var blockType = block.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            if (!string.Equals(blockType, "text", StringComparison.Ordinal))
            {
                continue;
            }

            foundTextBlock = true;
            var text = block.TryGetProperty("text", out var textElement) ? textElement.GetString() : null;
            if (!string.IsNullOrEmpty(text))
            {
                sb.Append(text);
            }
        }

        if (!foundTextBlock)
        {
            throw new InvalidOperationException($"HTTP response JSON contained no text content blocks ({context}).");
        }

        var textOut = sb.ToString();
        if (string.IsNullOrWhiteSpace(textOut))
        {
            throw new InvalidOperationException($"HTTP response JSON contained empty text content ({context}).");
        }

        return textOut;
    }

    private InvalidOperationException BuildNonSuccessStatusException(LlmHttpResponse response, string context)
    {
        var detail = ParseAnthropicError(response.Body);
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
        return new InvalidOperationException($"HTTP request failed with status {response.StatusCode} ({context}).{suffix}");
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
