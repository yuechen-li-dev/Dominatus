using System.Net.Http.Headers;
using System.Text;

namespace Dominatus.Actuators.Standard;

internal sealed record ResolvedHttpRequest(Uri Uri, IReadOnlyList<KeyValuePair<string, string>> Headers);

internal sealed class HttpRequestResolver
{
    private readonly ValidatedHttpActuatorOptions _options;

    public HttpRequestResolver(HttpActuatorOptions options)
        => _options = HttpActuatorValidation.Validate(options);

    public ValidatedHttpActuatorOptions Options => _options;

    public ResolvedHttpRequest Resolve(
        string endpointName,
        string path,
        IReadOnlyDictionary<string, string>? query,
        IReadOnlyDictionary<string, string>? headers)
    {
        if (string.IsNullOrWhiteSpace(endpointName))
            throw new ArgumentException("Endpoint is required.", nameof(endpointName));

        if (!_options.Endpoints.TryGetValue(endpointName, out var endpoint))
            throw new InvalidOperationException($"Unknown HTTP endpoint '{endpointName}'.");

        if (path is null)
            throw new ArgumentException("Path is required.", nameof(path));

        if (ContainsFragment(path))
            throw new InvalidOperationException("HTTP path fragments are not allowed.");

        if (Uri.TryCreate(path, UriKind.Absolute, out _)
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.StartsWith("\\\\", StringComparison.Ordinal))
            throw new InvalidOperationException("Absolute URLs are not allowed in HTTP commands.");

        var relative = path.Trim();
        if (relative.StartsWith("/", StringComparison.Ordinal))
            relative = relative[1..];

        var combined = new Uri(endpoint.BaseUri, relative);

        if (!SameOrigin(endpoint.BaseUri, combined))
            throw new InvalidOperationException("HTTP path cannot change scheme or host.");

        if (!IsContainedPath(endpoint.BaseUri, combined))
            throw new InvalidOperationException("HTTP path escapes the configured endpoint base path.");

        var withQuery = AppendQuery(combined, query);
        var resolvedHeaders = ValidateHeaders(headers);

        return new ResolvedHttpRequest(withQuery, resolvedHeaders);
    }

    private IReadOnlyList<KeyValuePair<string, string>> ValidateHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0)
            return [];

        var output = new List<KeyValuePair<string, string>>(headers.Count);
        foreach (var entry in headers)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
                throw new InvalidOperationException("HTTP header name is required.");

            if (HttpActuatorOptions.SensitiveHeaders.Contains(entry.Key))
                throw new InvalidOperationException($"HTTP header '{entry.Key}' is not allowed in M1.");

            if (!_options.AllowedRequestHeaders.Contains(entry.Key))
                throw new InvalidOperationException($"HTTP header '{entry.Key}' is not in the allowed request header list.");

            var value = entry.Value ?? string.Empty;
            output.Add(new KeyValuePair<string, string>(entry.Key.Trim(), value));
        }

        return output;
    }

    private static Uri AppendQuery(Uri uri, IReadOnlyDictionary<string, string>? query)
    {
        if (query is null || query.Count == 0)
            return uri;

        var builder = new UriBuilder(uri);
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(builder.Query))
            sb.Append(builder.Query.TrimStart('?'));

        foreach (var pair in query)
        {
            if (sb.Length > 0)
                sb.Append('&');

            sb.Append(Uri.EscapeDataString(pair.Key ?? string.Empty));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(pair.Value ?? string.Empty));
        }

        builder.Query = sb.ToString();
        return builder.Uri;
    }

    private static bool ContainsFragment(string path)
        => path.Contains('#', StringComparison.Ordinal);

    private static bool SameOrigin(Uri expected, Uri actual)
        => string.Equals(expected.Scheme, actual.Scheme, StringComparison.OrdinalIgnoreCase)
           && string.Equals(expected.Host, actual.Host, StringComparison.OrdinalIgnoreCase)
           && expected.Port == actual.Port;

    private static bool IsContainedPath(Uri baseUri, Uri candidate)
    {
        var baseSegments = GetPathSegments(baseUri.AbsolutePath);
        var candidateSegments = GetPathSegments(candidate.AbsolutePath);

        if (candidateSegments.Count < baseSegments.Count)
            return false;

        for (var i = 0; i < baseSegments.Count; i++)
        {
            if (!string.Equals(baseSegments[i], candidateSegments[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static List<string> GetPathSegments(string path)
        => path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
