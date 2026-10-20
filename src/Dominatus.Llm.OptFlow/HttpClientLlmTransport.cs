using System.Net.Http;

namespace Dominatus.Llm.OptFlow;

public sealed class HttpClientLlmTransport : ILlmHttpTransport
{
    private static readonly HttpClient SharedClient = new();
    private readonly HttpClient client;

    /// <summary>Injected clients are owned by the host/factory, not by this transport.</summary>
    public HttpClientLlmTransport() : this(SharedClient) { }
    public HttpClientLlmTransport(HttpClient client) => this.client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<LlmHttpResponse> SendAsync(LlmHttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var message = new HttpRequestMessage(request.Method, request.Uri)
        {
            Content = request.Body is null
                ? null
                : new StringContent(request.Body)
        };

        foreach (var header in request.Headers)
        {
            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                if (message.Content is null)
                {
                    throw new InvalidOperationException($"Cannot apply HTTP header '{header.Key}' without request content.");
                }

                // StringContent installs text/plain itself. Single-value content
                // headers must be replaced, otherwise TryAdd silently preserves
                // that default instead of the provider's declared JSON media type.
                message.Content.Headers.Remove(header.Key);
                if (!message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    throw new InvalidOperationException($"Cannot apply HTTP content header '{header.Key}'.");
            }
        }

        using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var body = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var responseHeader in response.Headers)
        {
            headers[responseHeader.Key] = string.Join(",", responseHeader.Value);
        }

        if (response.Content is not null)
        {
            foreach (var contentHeader in response.Content.Headers)
            {
                headers[contentHeader.Key] = string.Join(",", contentHeader.Value);
            }
        }

        return new LlmHttpResponse((int)response.StatusCode, body, headers);
    }
}
