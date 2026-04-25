using System.Text;
using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

public sealed class OpenAiResponsesLlmClient : LlmHttpTextClientBase, ILlmClient
{
    private const string UserAgent = "Dominatus.Llm.OptFlow/openai-responses-v1";
    private const string StatelessInstructions = "You are generating one bounded text response for a deterministic runtime. Follow the persona and intent. Return only the generated text.";

    public OpenAiResponsesLlmClient(LlmHttpProviderOptions options, ILlmHttpTransport transport)
        : base(options, transport)
    {
    }

    public async Task<LlmTextResult> GenerateTextAsync(
        LlmTextRequest request,
        string requestHash,
        CancellationToken cancellationToken)
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
            var message = Redact(ex.Message, Options.ApiKey);
            throw new InvalidOperationException($"Transport failure ({context}). {message}", ex);
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
            var status = root.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;

            if (IsExplicitFailureStatus(status))
            {
                throw new InvalidOperationException($"Response status '{status}' indicates failure ({context}).");
            }

            if (!root.TryGetProperty("output", out var outputElement) || outputElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"HTTP response JSON missing required output array ({context}).");
            }

            var text = AggregateOutputText(outputElement, context);
            var (inputTokens, outputTokens) = ParseUsage(root, context);

            return new LlmTextResult(
                Text: text,
                RequestHash: requestHash,
                Provider: Options.Provider,
                Model: Options.Model,
                FinishReason: string.IsNullOrWhiteSpace(status) ? null : status,
                InputTokens: inputTokens,
                OutputTokens: outputTokens);
        }
    }

    private static bool IsExplicitFailureStatus(string? status)
        => string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "incomplete", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);

    private static string AggregateOutputText(JsonElement outputElement, string context)
    {
        var builder = new StringBuilder();
        var foundOutputTextBlock = false;

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

                foundOutputTextBlock = true;

                var blockText = block.TryGetProperty("text", out var textElement) ? textElement.GetString() : null;
                if (!string.IsNullOrEmpty(blockText))
                {
                    builder.Append(blockText);
                }
            }
        }

        if (!foundOutputTextBlock)
        {
            throw new InvalidOperationException($"HTTP response JSON contained no output_text blocks ({context}).");
        }

        var aggregated = builder.ToString();
        if (string.IsNullOrWhiteSpace(aggregated))
        {
            throw new InvalidOperationException($"HTTP response JSON contained empty output text ({context}).");
        }

        return aggregated;
    }

    private static (int? InputTokens, int? OutputTokens) ParseUsage(JsonElement root, string context)
    {
        if (!root.TryGetProperty("usage", out var usageElement) || usageElement.ValueKind == JsonValueKind.Null)
        {
            return (null, null);
        }

        if (usageElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"HTTP response JSON usage was malformed ({context}).");
        }

        int? inputTokens = null;
        int? outputTokens = null;

        if (usageElement.TryGetProperty("input_tokens", out var inputTokenElement) && inputTokenElement.ValueKind != JsonValueKind.Null)
        {
            if (!inputTokenElement.TryGetInt32(out var parsedInput))
            {
                throw new InvalidOperationException($"HTTP response JSON usage.input_tokens was malformed ({context}).");
            }

            if (parsedInput < 0)
            {
                throw new InvalidOperationException($"HTTP response JSON has invalid negative input_tokens ({context}).");
            }

            inputTokens = parsedInput;
        }

        if (usageElement.TryGetProperty("output_tokens", out var outputTokenElement) && outputTokenElement.ValueKind != JsonValueKind.Null)
        {
            if (!outputTokenElement.TryGetInt32(out var parsedOutput))
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

    private InvalidOperationException BuildNonSuccessStatusException(LlmHttpResponse httpResponse, string context)
    {
        var detail = ParseOpenAiError(httpResponse.Body);
        var detailPart = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}";
        return new InvalidOperationException(
            $"HTTP request failed with status {httpResponse.StatusCode} ({context}).{detailPart}");
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

            var errorType = errorElement.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            var errorCode = errorElement.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
            var errorMessage = errorElement.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;

            errorMessage = Redact(errorMessage ?? string.Empty, Options.ApiKey);

            return $"OpenAI error type='{errorType ?? "unknown"}', code='{errorCode ?? "unknown"}', message='{errorMessage}'.";
        }
        catch (JsonException)
        {
            return "Response body was malformed JSON.";
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
            writer.WriteString("instructions", StatelessInstructions);

            writer.WritePropertyName("input");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("role", "user");

            writer.WritePropertyName("content");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("type", "input_text");
            writer.WriteString("text", inputText);
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteNumber("temperature", request.Sampling.Temperature);

            if (request.Sampling.MaxOutputTokens is not null)
            {
                writer.WriteNumber("max_output_tokens", request.Sampling.MaxOutputTokens.Value);
            }

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

    private static string BuildInputText(LlmTextRequest request)
        => $"StableId: {request.StableId}\nIntent: {request.Intent}\nPersona: {request.Persona}\nContextJson: {request.CanonicalContextJson}";
}
