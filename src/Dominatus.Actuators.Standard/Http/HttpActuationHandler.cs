using Dominatus.Core.Runtime;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Dominatus.Actuators.Standard;

public sealed class HttpActuationHandler :
    IActuationHandler<HttpGetTextCommand>,
    IActuationHandler<HttpPostJsonCommand>,
    IActuationHandler<HttpPostTextCommand>,
    IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly HttpRequestResolver _resolver;
    private readonly HttpClient _httpClient;
    private readonly long _maxRequestBytes;
    private readonly long _maxResponseBytes;
    private readonly bool _disposeClient;

    public HttpActuationHandler(HttpActuatorOptions options, HttpMessageHandler? messageHandler = null)
    {
        _resolver = new HttpRequestResolver(options ?? throw new ArgumentNullException(nameof(options)));

        _maxRequestBytes = _resolver.Options.MaxRequestBytes;
        _maxResponseBytes = _resolver.Options.MaxResponseBytes;

        if (messageHandler is null)
        {
            _httpClient = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = _resolver.Options.AllowRedirects,
                UseCookies = false,
                UseDefaultCredentials = false
            });
            _disposeClient = true;
        }
        else
        {
            _httpClient = new HttpClient(messageHandler, disposeHandler: false);
        }

        _httpClient.Timeout = _resolver.Options.Timeout;
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, HttpGetTextCommand cmd)
        => HandleCommand(() => SendAndReadAsync(HttpMethod.Get, cmd.Endpoint, cmd.Path, cmd.Query, cmd.Headers, content: null, ctx.Cancel));

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, HttpPostJsonCommand cmd)
        => HandleCommand(() =>
        {
            ArgumentNullException.ThrowIfNull(cmd);
            ValidateJson(cmd.Json);
            var content = BuildUtf8Content(cmd.Json, "application/json");
            return SendAndReadAsync(HttpMethod.Post, cmd.Endpoint, cmd.Path, cmd.Query, cmd.Headers, content, ctx.Cancel);
        });

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, HttpPostTextCommand cmd)
        => HandleCommand(() =>
        {
            ArgumentNullException.ThrowIfNull(cmd);
            if (string.IsNullOrWhiteSpace(cmd.ContentType))
                throw new InvalidOperationException("ContentType is required.");

            var content = BuildUtf8Content(cmd.Text ?? string.Empty, cmd.ContentType);
            return SendAndReadAsync(HttpMethod.Post, cmd.Endpoint, cmd.Path, cmd.Query, cmd.Headers, content, ctx.Cancel);
        });

    public void Dispose()
    {
        if (_disposeClient)
            _httpClient.Dispose();
    }

    private ActuatorHost.HandlerResult HandleCommand(Func<HttpTextResult> action)
    {
        try
        {
            var result = action();
            return ActuatorHost.HandlerResult.CompletedWithPayload(result);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or JsonException or HttpRequestException or OperationCanceledException)
        {
            return new ActuatorHost.HandlerResult(Accepted: true, Completed: true, Ok: false, Error: ex.Message);
        }
    }

    private HttpTextResult SendAndReadAsync(
        HttpMethod method,
        string endpoint,
        string path,
        IReadOnlyDictionary<string, string>? query,
        IReadOnlyDictionary<string, string>? headers,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, endpoint, path, query, headers, content);
        using var response = _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).GetAwaiter().GetResult();

        var text = ReadBoundedText(response.Content, _maxResponseBytes, cancellationToken);
        var resultHeaders = CollectHeaders(response);

        return new HttpTextResult((int)response.StatusCode, response.IsSuccessStatusCode, text, resultHeaders);
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string endpoint,
        string path,
        IReadOnlyDictionary<string, string>? query,
        IReadOnlyDictionary<string, string>? headers,
        HttpContent? content)
    {
        var resolved = _resolver.Resolve(endpoint, path, query, headers);

        var request = new HttpRequestMessage(method, resolved.Uri)
        {
            Content = content
        };

        foreach (var header in resolved.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return request;
    }

    private StringContent BuildUtf8Content(string text, string contentType)
    {
        var byteCount = Utf8.GetByteCount(text ?? string.Empty);
        if (byteCount > _maxRequestBytes)
            throw new InvalidOperationException($"HTTP request body exceeds MaxRequestBytes ({_maxRequestBytes}).");

        var content = new StringContent(text ?? string.Empty, Utf8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return content;
    }

    private static void ValidateJson(string json)
    {
        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (Exception ex) when (ex is ArgumentNullException or JsonException)
        {
            throw new InvalidOperationException("Invalid JSON payload for HttpPostJsonCommand.", ex);
        }
    }

    private static string ReadBoundedText(HttpContent content, long maxResponseBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long len && len > maxResponseBytes)
            throw new InvalidOperationException($"HTTP response body exceeds MaxResponseBytes ({maxResponseBytes}).");

        using var stream = content.ReadAsStream(cancellationToken);
        using var buffer = new MemoryStream();

        var chunk = new byte[8192];
        while (true)
        {
            var read = stream.Read(chunk, 0, chunk.Length);
            if (read <= 0)
                break;

            if (buffer.Length + read > maxResponseBytes)
                throw new InvalidOperationException($"HTTP response body exceeds MaxResponseBytes ({maxResponseBytes}).");

            buffer.Write(chunk, 0, read);
        }

        var encoding = ResolveEncoding(content.Headers.ContentType);
        return encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static Encoding ResolveEncoding(MediaTypeHeaderValue? contentType)
    {
        var charset = contentType?.CharSet;
        if (string.IsNullOrWhiteSpace(charset))
            return Utf8;

        try
        {
            return Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            return Utf8;
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> CollectHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers)
            headers[header.Key] = header.Value.ToArray();

        foreach (var header in response.Content.Headers)
            headers[header.Key] = header.Value.ToArray();

        return headers;
    }
}
