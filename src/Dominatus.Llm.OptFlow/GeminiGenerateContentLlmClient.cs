using System.Text;
using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

public sealed class GeminiGenerateContentLlmClient : LlmHttpTextClientBase, ILlmClient
{
    private const string UserAgent = "Dominatus.Llm.OptFlow/gemini-generatecontent-v1";
    private const string StatelessInstructions = "You are generating one bounded text response for a deterministic runtime. Follow the persona and intent. Return only the generated text.";
    private const int DefaultMaxOutputTokens = 64;

    public GeminiGenerateContentLlmClient(LlmHttpProviderOptions options, ILlmHttpTransport transport)
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
                ["x-goog-api-key"] = Options.ApiKey,
                ["Content-Type"] = "application/json",
                ["Accept"] = "application/json",
                ["User-Agent"] = UserAgent,
            },
            Body: BuildRequestBody(request));

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
            var text = AggregateCandidateText(root, context);
            var (inputTokens, outputTokens) = ParseUsage(root, context);
            var finishReason = FindFinishReason(root);

            return new LlmTextResult(
                Text: text,
                RequestHash: requestHash,
                Provider: Options.Provider,
                Model: Options.Model,
                FinishReason: finishReason,
                InputTokens: inputTokens,
                OutputTokens: outputTokens);
        }
    }

    private static string BuildRequestBody(LlmTextRequest request)
    {
        var inputText = BuildInputText(request);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            writer.WritePropertyName("system_instruction");
            writer.WriteStartObject();
            writer.WritePropertyName("parts");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("text", StatelessInstructions);
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
            writer.WriteString("text", inputText);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WritePropertyName("generationConfig");
            writer.WriteStartObject();
            writer.WriteNumber("temperature", request.Sampling.Temperature);
            writer.WriteNumber("maxOutputTokens", request.Sampling.MaxOutputTokens ?? DefaultMaxOutputTokens);
            writer.WriteNumber("topP", request.Sampling.TopP ?? 1.0);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildInputText(LlmTextRequest request)
        => $"StableId: {request.StableId}\nIntent: {request.Intent}\nPersona: {request.Persona}\nContextJson: {request.CanonicalContextJson}";

    private static string AggregateCandidateText(JsonElement root, string context)
    {
        if (!root.TryGetProperty("candidates", out var candidatesElement) || candidatesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"HTTP response JSON missing required candidates array ({context}).");
        }

        var builder = new StringBuilder();
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
                    builder.Append(text);
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

        var aggregate = builder.ToString();
        if (string.IsNullOrWhiteSpace(aggregate))
        {
            throw new InvalidOperationException($"HTTP response JSON contained empty candidate text ({context}).");
        }

        return aggregate;
    }

    private static (int? InputTokens, int? OutputTokens) ParseUsage(JsonElement root, string context)
    {
        if (!root.TryGetProperty("usageMetadata", out var usageElement) || usageElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return (null, null);
        }

        if (usageElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"HTTP response JSON usageMetadata was malformed ({context}).");
        }

        int? inputTokens = null;
        int? outputTokens = null;

        if (usageElement.TryGetProperty("promptTokenCount", out var promptElement) && promptElement.ValueKind != JsonValueKind.Null)
        {
            if (!promptElement.TryGetInt32(out var parsedPrompt))
            {
                throw new InvalidOperationException($"HTTP response JSON usageMetadata.promptTokenCount was malformed ({context}).");
            }

            if (parsedPrompt < 0)
            {
                throw new InvalidOperationException($"HTTP response JSON has invalid negative promptTokenCount ({context}).");
            }

            inputTokens = parsedPrompt;
        }

        if (usageElement.TryGetProperty("candidatesTokenCount", out var candidateElement) && candidateElement.ValueKind != JsonValueKind.Null)
        {
            if (!candidateElement.TryGetInt32(out var parsedCandidate))
            {
                throw new InvalidOperationException($"HTTP response JSON usageMetadata.candidatesTokenCount was malformed ({context}).");
            }

            if (parsedCandidate < 0)
            {
                throw new InvalidOperationException($"HTTP response JSON has invalid negative candidatesTokenCount ({context}).");
            }

            outputTokens = parsedCandidate;
        }

        return (inputTokens, outputTokens);
    }

    private static string? FindFinishReason(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidatesElement) || candidatesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var candidate in candidatesElement.EnumerateArray())
        {
            var finishReason = candidate.TryGetProperty("finishReason", out var finishElement) ? finishElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(finishReason))
            {
                return finishReason;
            }
        }

        return null;
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
        var detailPart = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
        return new InvalidOperationException($"HTTP request failed with status {response.StatusCode} ({context}).{detailPart}");
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
