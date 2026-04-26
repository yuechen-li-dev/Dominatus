namespace Dominatus.Actuators.Standard;

public sealed record HttpActuatorOptions
{
    public static readonly IReadOnlySet<string> DefaultAllowedRequestHeaders =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Accept",
            "User-Agent",
            "Content-Type"
        };

    internal static readonly IReadOnlySet<string> SensitiveHeaders =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization",
            "Cookie",
            "Set-Cookie",
            "Proxy-Authorization",
            "X-Api-Key",
            "X-Auth-Token"
        };

    public IReadOnlyList<AllowedHttpEndpoint> Endpoints { get; init; } = [];
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    public long MaxResponseBytes { get; init; } = 1_000_000;
    public long MaxRequestBytes { get; init; } = 100_000;
    public bool AllowRedirects { get; init; }
    public IReadOnlySet<string> AllowedRequestHeaders { get; init; } = DefaultAllowedRequestHeaders;
}

internal sealed record ValidatedHttpActuatorOptions(
    IReadOnlyDictionary<string, AllowedHttpEndpoint> Endpoints,
    TimeSpan Timeout,
    long MaxResponseBytes,
    long MaxRequestBytes,
    bool AllowRedirects,
    IReadOnlySet<string> AllowedRequestHeaders);

internal static class HttpActuatorValidation
{
    public static ValidatedHttpActuatorOptions Validate(HttpActuatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.Timeout), "Timeout must be positive.");

        if (options.MaxResponseBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxResponseBytes), "MaxResponseBytes must be positive.");

        if (options.MaxRequestBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxRequestBytes), "MaxRequestBytes must be positive.");

        if (options.Endpoints is null || options.Endpoints.Count == 0)
            throw new ArgumentException("At least one HTTP endpoint is required.", nameof(options.Endpoints));

        if (options.AllowedRequestHeaders is null || options.AllowedRequestHeaders.Count == 0)
            throw new ArgumentException("At least one allowed HTTP request header is required.", nameof(options.AllowedRequestHeaders));

        var endpoints = new Dictionary<string, AllowedHttpEndpoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in options.Endpoints)
        {
            if (endpoint is null)
                throw new ArgumentException("HTTP endpoint entry cannot be null.", nameof(options.Endpoints));

            if (!endpoints.TryAdd(endpoint.Name, endpoint))
                throw new ArgumentException($"Duplicate HTTP endpoint name '{endpoint.Name}'.", nameof(options.Endpoints));
        }

        var allowedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in options.AllowedRequestHeaders)
        {
            if (string.IsNullOrWhiteSpace(header))
                throw new ArgumentException("Allowed request header names cannot be empty.", nameof(options.AllowedRequestHeaders));

            if (!IsValidHeaderToken(header))
                throw new ArgumentException($"Invalid HTTP header name '{header}'.", nameof(options.AllowedRequestHeaders));

            if (HttpActuatorOptions.SensitiveHeaders.Contains(header))
                throw new ArgumentException($"Sensitive HTTP header '{header}' is not supported in M1.", nameof(options.AllowedRequestHeaders));

            allowedHeaders.Add(header.Trim());
        }

        return new ValidatedHttpActuatorOptions(
            Endpoints: endpoints,
            Timeout: options.Timeout,
            MaxResponseBytes: options.MaxResponseBytes,
            MaxRequestBytes: options.MaxRequestBytes,
            AllowRedirects: options.AllowRedirects,
            AllowedRequestHeaders: allowedHeaders);
    }

    private static bool IsValidHeaderToken(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var isToken = char.IsLetterOrDigit(c) || c is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
            if (!isToken)
                return false;
        }

        return true;
    }
}
