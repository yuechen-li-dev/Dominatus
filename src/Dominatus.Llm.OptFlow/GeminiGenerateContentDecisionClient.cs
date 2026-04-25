using System.Text;
using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

public sealed class GeminiGenerateContentDecisionClient : LlmHttpTextClientBase, ILlmDecisionClient
{
    private const string UserAgent = "Dominatus.Llm.OptFlow/gemini-generatecontent-decision-v1";
    private const string DecisionInstructions = "You are scoring a closed option set for a deterministic runtime. Return only strict JSON matching the schema.";
    private const int DefaultMaxOutputTokens = 512;

    public GeminiGenerateContentDecisionClient(LlmHttpProviderOptions options, ILlmHttpTransport transport)
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
                ["x-goog-api-key"] = Options.ApiKey,
                ["Content-Type"] = "application/json",
                ["Accept"] = "application/json",
                ["User-Agent"] = UserAgent,
            },
            Body: BuildRequestBody(request));

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
            var text = AggregateText(root, context);
            return LlmDecisionJsonParser.ParseAndValidate(text, request, requestHash, context);
        }
    }

    private static string BuildRequestBody(LlmDecisionRequest request)
    {
        var prompt = LlmDecisionPromptBuilder.BuildPrompt(request);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            writer.WritePropertyName("system_instruction");
            writer.WriteStartObject();
            writer.WritePropertyName("parts");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("text", DecisionInstructions);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();

            writer.WritePropertyName("contents");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WritePropertyName("parts");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("text", prompt);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WritePropertyName("generationConfig");
            writer.WriteStartObject();
            writer.WriteNumber("temperature", request.Sampling.Temperature);
            writer.WriteNumber("maxOutputTokens", request.Sampling.MaxOutputTokens ?? DefaultMaxOutputTokens);
            if (request.Sampling.TopP is not null)
            {
                writer.WriteNumber("topP", request.Sampling.TopP.Value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string AggregateText(JsonElement root, string context)
    {
        if (!root.TryGetProperty("candidates", out var candidatesElement) || candidatesElement.ValueKind != JsonValueKind.Array)
        {
            if (TryGetPromptBlockReason(root, out var blockReason))
            {
                throw new InvalidOperationException($"Provider blocked prompt with blockReason='{blockReason}' ({context}).");
            }

            throw new InvalidOperationException($"HTTP response JSON missing required candidates array ({context}).");
        }

        var sb = new StringBuilder();
        var foundTextPart = false;

        foreach (var candidate in candidatesElement.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var contentElement) || contentElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!contentElement.TryGetProperty("parts", out var partsElement) || partsElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in partsElement.EnumerateArray())
            {
                if (!part.TryGetProperty("text", out var textElement))
                {
                    continue;
                }

                foundTextPart = true;
                var text = textElement.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    sb.Append(text);
                }
            }
        }

        if (!foundTextPart)
        {
            if (TryGetPromptBlockReason(root, out var blockReason))
            {
                throw new InvalidOperationException($"Provider blocked prompt with blockReason='{blockReason}' ({context}).");
            }

            throw new InvalidOperationException($"HTTP response JSON contained no candidate text parts ({context}).");
        }

        var textOut = sb.ToString();
        if (string.IsNullOrWhiteSpace(textOut))
        {
            throw new InvalidOperationException($"HTTP response JSON contained empty candidate text ({context}).");
        }

        return textOut;
    }

    private static bool TryGetPromptBlockReason(JsonElement root, out string? blockReason)
    {
        blockReason = null;
        if (!root.TryGetProperty("promptFeedback", out var promptFeedbackElement) || promptFeedbackElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        blockReason = promptFeedbackElement.TryGetProperty("blockReason", out var blockReasonElement)
            ? blockReasonElement.GetString()
            : null;

        return !string.IsNullOrWhiteSpace(blockReason);
    }

    private InvalidOperationException BuildNonSuccessStatusException(LlmHttpResponse response, string context)
    {
        var detail = ParseGeminiError(response.Body);
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
        return new InvalidOperationException($"HTTP request failed with status {response.StatusCode} ({context}).{suffix}");
    }

    private string ParseGeminiError(string? responseBody)
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
                return "Response body did not contain a Google error object.";
            }

            var code = errorElement.TryGetProperty("code", out var codeElement) ? codeElement.GetRawText() : "unknown";
            var status = errorElement.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
            var message = errorElement.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
            return $"Google error code='{code}', status='{status ?? "unknown"}', message='{Sanitize(message ?? string.Empty)}'.";
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
