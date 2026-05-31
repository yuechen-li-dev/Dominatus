using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dominatus.Llm.OptFlow;

public sealed record OpenRouterLlmClientOptions
{
    private const int MaxHeaderValueLength = 512;

    private string _apiKey = string.Empty;
    private string _model = string.Empty;
    private Uri _endpoint = new("https://openrouter.ai/api/v1/chat/completions");
    private string? _httpReferer;
    private string? _title;
    private string _providerId = "openrouter";
    private TimeSpan _timeout = TimeSpan.FromSeconds(60);

    public required string ApiKey
    {
        get => _apiKey;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _apiKey = value;
        }
    }

    public required string Model
    {
        get => _model;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _model = value;
        }
    }

    public Uri Endpoint
    {
        get => _endpoint;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!value.IsAbsoluteUri)
            {
                throw new ArgumentException("Endpoint must be an absolute URI.", nameof(Endpoint));
            }

            _endpoint = value;
        }
    }

    public string? HttpReferer
    {
        get => _httpReferer;
        init
        {
            if (value is { Length: > MaxHeaderValueLength })
            {
                throw new ArgumentOutOfRangeException(nameof(HttpReferer), $"HttpReferer cannot exceed {MaxHeaderValueLength} characters.");
            }

            _httpReferer = value;
        }
    }

    public string? Title
    {
        get => _title;
        init
        {
            if (value is { Length: > MaxHeaderValueLength })
            {
                throw new ArgumentOutOfRangeException(nameof(Title), $"Title cannot exceed {MaxHeaderValueLength} characters.");
            }

            _title = value;
        }
    }

    public string ProviderId
    {
        get => _providerId;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _providerId = value;
        }
    }

    public TimeSpan Timeout
    {
        get => _timeout;
        init
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout must be greater than zero.");
            }

            _timeout = value;
        }
    }
}

public sealed class OpenRouterLlmClient : ILlmClient
{
    private const string UserAgent = "Dominatus.Llm.OptFlow/openrouter-v1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly OpenRouterLlmClientOptions _options;

    public OpenRouterLlmClient(HttpClient httpClient, OpenRouterLlmClientOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = ValidateOptions(options);
    }

    public Task<LlmTextResult> CompleteAsync(LlmTextRequest request, CancellationToken cancellationToken = default)
        => GenerateTextAsync(request, LlmRequestHasher.ComputeHash(request), cancellationToken);

    private static OpenRouterLlmClientOptions ValidateOptions(OpenRouterLlmClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ProviderId);
        ArgumentNullException.ThrowIfNull(options.Endpoint);
        if (!options.Endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Endpoint must be an absolute URI.", nameof(options));
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Timeout must be greater than zero.");
        }

        return options;
    }

    public async Task<LlmTextResult> GenerateTextAsync(
        LlmTextRequest request,
        string requestHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        cancellationToken.ThrowIfCancellationRequested();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.Timeout);

        using var httpRequest = BuildHttpRequest(request);
        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new LlmProviderTransientException($"OpenRouter request timed out (providerId='{_options.ProviderId}').", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmProviderTransientException($"OpenRouter network request failed (providerId='{_options.ProviderId}'). {Sanitize(ex.Message)}", ex);
        }

        using (httpResponse)
        {
            if (!httpResponse.IsSuccessStatusCode)
            {
                throw BuildNonSuccessException(httpResponse);
            }

            string body;
            try
            {
                body = await httpResponse.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                throw new LlmProviderTransientException($"OpenRouter response read timed out (providerId='{_options.ProviderId}').", ex);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                throw new LlmProviderTransientException($"OpenRouter response body was empty (providerId='{_options.ProviderId}').");
            }

            OpenRouterChatResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<OpenRouterChatResponse>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new LlmProviderTransientException($"OpenRouter response JSON was malformed (providerId='{_options.ProviderId}'). {Sanitize(ex.Message)}", ex);
            }

            var choice = response?.Choices?.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Message?.Content));
            var text = choice?.Message?.Content;
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new LlmProviderTransientException($"OpenRouter response contained no assistant content (providerId='{_options.ProviderId}').");
            }

            return new LlmTextResult(
                Text: text,
                RequestHash: requestHash,
                Provider: _options.ProviderId,
                Model: string.IsNullOrWhiteSpace(response?.Model) ? _options.Model : response.Model,
                FinishReason: choice?.FinishReason,
                InputTokens: response?.Usage?.PromptTokens,
                OutputTokens: response?.Usage?.CompletionTokens,
                ProviderId: _options.ProviderId);
        }
    }

    public static OpenRouterLlmClient FromEnvironment(HttpClient httpClient, string model, string envVar = "OPENROUTER_API_KEY")
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(envVar);

        return new OpenRouterLlmClient(
            httpClient,
            new OpenRouterLlmClientOptions
            {
                ApiKey = Environment.GetEnvironmentVariable(envVar) ?? throw new InvalidOperationException($"Environment variable '{envVar}' is required for OpenRouter."),
                Model = model,
            });
    }

    private HttpRequestMessage BuildHttpRequest(LlmTextRequest request)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = new StringContent(BuildRequestBody(request), Encoding.UTF8, "application/json"),
        };

        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpRequest.Headers.UserAgent.ParseAdd(UserAgent);

        if (!string.IsNullOrWhiteSpace(_options.HttpReferer))
        {
            httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", _options.HttpReferer);
        }

        if (!string.IsNullOrWhiteSpace(_options.Title))
        {
            httpRequest.Headers.TryAddWithoutValidation("X-Title", _options.Title);
        }

        return httpRequest;
    }

    private string BuildRequestBody(LlmTextRequest request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", _options.Model);
            writer.WriteBoolean("stream", false);
            writer.WriteNumber("temperature", request.Sampling.Temperature);
            if (request.Sampling.TopP is { } topP)
            {
                writer.WriteNumber("top_p", topP);
            }

            if (request.Sampling.MaxOutputTokens is { } maxOutputTokens)
            {
                writer.WriteNumber("max_tokens", maxOutputTokens);
            }

            writer.WriteStartArray("messages");
            writer.WriteStartObject();
            writer.WriteString("role", "system");
            writer.WriteString("content", request.Persona);
            writer.WriteEndObject();
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteString("content", BuildUserMessage(request));
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildUserMessage(LlmTextRequest request)
        => $"StableId: {request.StableId}\nIntent: {request.Intent}\nContextJson: {request.CanonicalContextJson}";

    private Exception BuildNonSuccessException(HttpResponseMessage response)
    {
        var statusCode = response.StatusCode;
        var retryAfter = TryParseRetryAfter(response.Headers.RetryAfter);
        var message = $"OpenRouter request failed with HTTP status {(int)statusCode} ({statusCode}) (providerId='{_options.ProviderId}').";

        return statusCode switch
        {
            HttpStatusCode.TooManyRequests => new LlmProviderRateLimitedException(message, retryAfter),
            HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => new LlmProviderTransientException(message),
            >= HttpStatusCode.InternalServerError => new LlmProviderTransientException(message),
            HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new LlmProviderException(message, isFallbackEligible: false),
            _ when (int)statusCode >= 400 && (int)statusCode < 500 => new LlmProviderException(message, isFallbackEligible: false),
            _ => new LlmProviderTransientException(message),
        };
    }

    private static TimeSpan? TryParseRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : null;
        }

        return null;
    }

    private string Sanitize(string message)
        => string.IsNullOrEmpty(message)
            ? string.Empty
            : message.Replace(_options.ApiKey, "[REDACTED]", StringComparison.Ordinal);

    private sealed record OpenRouterChatResponse(
        string? Id,
        string? Model,
        IReadOnlyList<OpenRouterChoice>? Choices,
        OpenRouterUsage? Usage);

    private sealed record OpenRouterChoice(
        OpenRouterMessage? Message,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record OpenRouterMessage(string? Role, string? Content);

    private sealed record OpenRouterUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
        [property: JsonPropertyName("total_tokens")] int? TotalTokens);
}
