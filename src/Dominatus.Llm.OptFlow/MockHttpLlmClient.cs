using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

public sealed class MockHttpLlmClient : LlmHttpTextClientBase, ILlmClient
{
    private const string UserAgent = "Dominatus.Llm.OptFlow/mock-http-v1";

    public MockHttpLlmClient(LlmHttpProviderOptions options, ILlmHttpTransport transport)
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

        JsonElement parsedContext;
        try
        {
            using var doc = JsonDocument.Parse(request.CanonicalContextJson);
            parsedContext = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid canonical context JSON ({context}). {ex.Message}", ex);
        }

        var bodyPayload = new
        {
            model = Options.Model,
            input = new
            {
                stableId = request.StableId,
                intent = request.Intent,
                persona = request.Persona,
                context = parsedContext,
            },
            sampling = new
            {
                temperature = request.Sampling.Temperature,
                maxOutputTokens = request.Sampling.MaxOutputTokens,
                topP = request.Sampling.TopP,
            }
        };

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
            Body: JsonSerializer.Serialize(bodyPayload, SerializerOptions));

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
            throw new InvalidOperationException(
                $"HTTP request failed with status {httpResponse.StatusCode} ({context}).");
        }

        if (string.IsNullOrWhiteSpace(httpResponse.Body))
        {
            throw new InvalidOperationException($"HTTP response body was empty ({context}).");
        }

        MockProviderResponse response;
        try
        {
            response = JsonSerializer.Deserialize<MockProviderResponse>(httpResponse.Body, SerializerOptions)
                ?? throw new InvalidOperationException($"HTTP response JSON was null ({context}).");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Malformed HTTP response JSON ({context}). {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(response.Text))
        {
            throw new InvalidOperationException($"HTTP response JSON missing required non-empty text ({context}).");
        }

        if (response.Usage?.InputTokens < 0)
        {
            throw new InvalidOperationException($"HTTP response JSON has invalid negative inputTokens ({context}).");
        }

        if (response.Usage?.OutputTokens < 0)
        {
            throw new InvalidOperationException($"HTTP response JSON has invalid negative outputTokens ({context}).");
        }

        return new LlmTextResult(
            Text: response.Text,
            RequestHash: requestHash,
            Provider: Options.Provider,
            Model: Options.Model,
            FinishReason: response.FinishReason,
            InputTokens: response.Usage?.InputTokens,
            OutputTokens: response.Usage?.OutputTokens);
    }

    private sealed class MockProviderResponse
    {
        public string? Text { get; set; }

        public string? FinishReason { get; set; }

        public MockUsage? Usage { get; set; }
    }

    private sealed class MockUsage
    {
        public int? InputTokens { get; set; }

        public int? OutputTokens { get; set; }
    }
}
