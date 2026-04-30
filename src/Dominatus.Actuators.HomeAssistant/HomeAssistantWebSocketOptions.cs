namespace Dominatus.Actuators.HomeAssistant;

public sealed record HomeAssistantWebSocketOptions
{
    public Uri BaseUri { get; init; } = null!;
    public string AccessToken { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedEntities { get; init; } = [];
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public int MaxMessageBytes { get; init; } = 1_000_000;
}

internal sealed record ValidatedHomeAssistantWebSocketOptions(
    Uri WebSocketUri,
    string AccessToken,
    IReadOnlySet<string> AllowedEntities,
    TimeSpan ConnectTimeout,
    int MaxMessageBytes);

internal static class HomeAssistantWebSocketValidation
{
    public static ValidatedHomeAssistantWebSocketOptions Validate(HomeAssistantWebSocketOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.BaseUri);

        if (!options.BaseUri.IsAbsoluteUri)
            throw new ArgumentException("BaseUri must be absolute.", nameof(options.BaseUri));

        if (!string.Equals(options.BaseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.BaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("BaseUri scheme must be http or https.", nameof(options.BaseUri));

        if (!string.IsNullOrEmpty(options.BaseUri.UserInfo))
            throw new ArgumentException("BaseUri must not include user info.", nameof(options.BaseUri));

        if (string.IsNullOrWhiteSpace(options.AccessToken))
            throw new ArgumentException("AccessToken is required.", nameof(options.AccessToken));

        if (options.ConnectTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.ConnectTimeout), "ConnectTimeout must be positive.");

        if (options.MaxMessageBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxMessageBytes), "MaxMessageBytes must be positive.");

        var allowedEntities = ValidateAllowedEntities(options.AllowedEntities);
        if (allowedEntities.Count == 0)
            throw new ArgumentException("AllowedEntities is required and must be non-empty.", nameof(options.AllowedEntities));

        return new ValidatedHomeAssistantWebSocketOptions(
            WebSocketUri: NormalizeWebSocketUri(options.BaseUri),
            AccessToken: options.AccessToken.Trim(),
            AllowedEntities: allowedEntities,
            ConnectTimeout: options.ConnectTimeout,
            MaxMessageBytes: options.MaxMessageBytes);
    }

    internal static Uri NormalizeWebSocketUri(Uri baseUri)
    {
        var wsScheme = string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";

        var builder = new UriBuilder(baseUri)
        {
            Scheme = wsScheme,
            Port = baseUri.IsDefaultPort ? -1 : baseUri.Port,
            Fragment = string.Empty,
            Query = string.Empty
        };

        var path = builder.Path ?? string.Empty;
        if (!path.EndsWith("/", StringComparison.Ordinal))
            path += "/";

        if (!path.EndsWith("/api/", StringComparison.OrdinalIgnoreCase))
            path += "api/";

        path += "websocket";

        builder.Path = path;
        return builder.Uri;
    }

    private static IReadOnlySet<string> ValidateAllowedEntities(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
            throw new ArgumentException("AllowedEntities is required and must be non-empty.", nameof(values));

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Allowed entity IDs are required and cannot be empty.", nameof(values));

            var normalized = value.Trim();
            if (!set.Add(normalized))
                throw new ArgumentException($"Duplicate allowed entity '{normalized}'.", nameof(values));
        }

        return set;
    }
}
