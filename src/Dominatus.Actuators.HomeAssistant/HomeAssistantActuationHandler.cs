using Dominatus.Core.Runtime;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Dominatus.Actuators.HomeAssistant;

public sealed class HomeAssistantActuationHandler :
    IActuationHandler<GetHomeAssistantStateCommand>,
    IActuationHandler<CallHomeAssistantServiceCommand>,
    IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly HomeAssistantRequestResolver _resolver;
    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;

    public HomeAssistantActuationHandler(HomeAssistantActuatorOptions options, HttpMessageHandler? messageHandler = null)
    {
        _resolver = new HomeAssistantRequestResolver(options ?? throw new ArgumentNullException(nameof(options)));

        if (messageHandler is null)
        {
            _httpClient = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
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

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, GetHomeAssistantStateCommand cmd)
    {
        try
        {
            var request = _resolver.Resolve(cmd);
            using var http = CreateRequest(HttpMethod.Get, request.Uri, content: null);
            using var response = _httpClient.SendAsync(http, HttpCompletionOption.ResponseHeadersRead, ctx.Cancel).GetAwaiter().GetResult();

            var text = ReadBoundedText(response.Content, _resolver.Options.MaxResponseBytes, ctx.Cancel);
            if (!response.IsSuccessStatusCode)
                return Fail($"Home Assistant state read failed with status {(int)response.StatusCode} ({response.StatusCode}): {TrimForError(text)}");

            var result = ParseStateResult(text, request.EntityId);
            return ActuatorHost.HandlerResult.CompletedWithPayload(result);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or OperationCanceledException or JsonException)
        {
            return Fail(SanitizeError(ex));
        }
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, CallHomeAssistantServiceCommand cmd)
    {
        try
        {
            var request = _resolver.Resolve(cmd);
            using var content = new StringContent(request.JsonData, Utf8);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

            using var http = CreateRequest(HttpMethod.Post, request.Uri, content);
            using var response = _httpClient.SendAsync(http, HttpCompletionOption.ResponseHeadersRead, ctx.Cancel).GetAwaiter().GetResult();

            var text = ReadBoundedText(response.Content, _resolver.Options.MaxResponseBytes, ctx.Cancel);
            var result = new HomeAssistantServiceCallResult((int)response.StatusCode, response.IsSuccessStatusCode, text);
            return ActuatorHost.HandlerResult.CompletedWithPayload(result);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or OperationCanceledException)
        {
            return Fail(SanitizeError(ex));
        }
    }

    public void Dispose()
    {
        if (_disposeClient)
            _httpClient.Dispose();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = content
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _resolver.Options.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static HomeAssistantEntityStateResult ParseStateResult(string json, string fallbackEntityId)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Home Assistant state response must be a JSON object.");

        var root = document.RootElement;

        var entityId = fallbackEntityId;
        if (TryGetPropertyIgnoreCase(root, "entity_id", out var entityProperty) && entityProperty.ValueKind == JsonValueKind.String)
            entityId = entityProperty.GetString() ?? fallbackEntityId;

        var state = string.Empty;
        if (TryGetPropertyIgnoreCase(root, "state", out var stateProperty))
            state = stateProperty.ValueKind == JsonValueKind.String ? stateProperty.GetString() ?? string.Empty : stateProperty.ToString();

        return new HomeAssistantEntityStateResult(entityId, state, json);
    }

    private static string ReadBoundedText(HttpContent content, long maxResponseBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long len && len > maxResponseBytes)
            throw new InvalidOperationException($"Home Assistant response body exceeds MaxResponseBytes ({maxResponseBytes}).");

        using var stream = content.ReadAsStream(cancellationToken);
        using var buffer = new MemoryStream();

        var chunk = new byte[8192];
        while (true)
        {
            var read = stream.Read(chunk, 0, chunk.Length);
            if (read <= 0)
                break;

            if (buffer.Length + read > maxResponseBytes)
                throw new InvalidOperationException($"Home Assistant response body exceeds MaxResponseBytes ({maxResponseBytes}).");

            buffer.Write(chunk, 0, read);
        }

        return Utf8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static ActuatorHost.HandlerResult Fail(string message)
        => new(Accepted: true, Completed: true, Ok: false, Error: message);

    private static string SanitizeError(Exception ex)
    {
        if (ex is OperationCanceledException)
            return "Home Assistant request timed out or was canceled.";

        return ex.Message;
    }

    private static string TrimForError(string text)
    {
        const int maxLength = 200;
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
