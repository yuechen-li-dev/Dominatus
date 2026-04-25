namespace Dominatus.Llm.OptFlow;

public interface ILlmHttpTransport
{
    Task<LlmHttpResponse> SendAsync(
        LlmHttpRequest request,
        CancellationToken cancellationToken);
}

public sealed record LlmHttpRequest(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    string Body);

public sealed record LlmHttpResponse(
    int StatusCode,
    string Body,
    IReadOnlyDictionary<string, string>? Headers = null);
