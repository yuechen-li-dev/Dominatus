namespace Dominatus.Actuators.HomeAssistant;

public sealed record HomeAssistantActuatorOptions
{
    public Uri BaseUri { get; init; } = null!;
    public string AccessToken { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedEntities { get; init; } = [];
    public IReadOnlyList<AllowedHomeAssistantService> AllowedServices { get; init; } = [];
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    public long MaxResponseBytes { get; init; } = 1_000_000;
    public long MaxRequestBytes { get; init; } = 100_000;
}

public sealed record AllowedHomeAssistantService(
    string Domain,
    string Service,
    IReadOnlyList<string>? AllowedEntityIds = null);

internal sealed record ValidatedHomeAssistantActuatorOptions(
    Uri ApiBaseUri,
    string AccessToken,
    IReadOnlySet<string> AllowedEntities,
    IReadOnlyDictionary<string, ValidatedAllowedHomeAssistantService> AllowedServices,
    TimeSpan Timeout,
    long MaxResponseBytes,
    long MaxRequestBytes);

internal sealed record ValidatedAllowedHomeAssistantService(
    string Domain,
    string Service,
    IReadOnlySet<string> AllowedEntityIds);

internal static class HomeAssistantActuatorValidation
{
    public static ValidatedHomeAssistantActuatorOptions Validate(HomeAssistantActuatorOptions options)
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

        if (options.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.Timeout), "Timeout must be positive.");

        if (options.MaxResponseBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxResponseBytes), "MaxResponseBytes must be positive.");

        if (options.MaxRequestBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxRequestBytes), "MaxRequestBytes must be positive.");

        var allowedEntities = ValidateAllowedEntities(options.AllowedEntities);
        var allowedServices = ValidateAllowedServices(options.AllowedServices);

        if (allowedEntities.Count == 0 && allowedServices.Count == 0)
            throw new ArgumentException("At least one allowed entity or allowed service is required.", nameof(options));

        return new ValidatedHomeAssistantActuatorOptions(
            ApiBaseUri: NormalizeApiBaseUri(options.BaseUri),
            AccessToken: options.AccessToken.Trim(),
            AllowedEntities: allowedEntities,
            AllowedServices: allowedServices,
            Timeout: options.Timeout,
            MaxResponseBytes: options.MaxResponseBytes,
            MaxRequestBytes: options.MaxRequestBytes);
    }

    private static IReadOnlySet<string> ValidateAllowedEntities(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

    private static IReadOnlyDictionary<string, ValidatedAllowedHomeAssistantService> ValidateAllowedServices(IReadOnlyList<AllowedHomeAssistantService>? services)
    {
        if (services is null || services.Count == 0)
            return new Dictionary<string, ValidatedAllowedHomeAssistantService>(StringComparer.OrdinalIgnoreCase);

        var output = new Dictionary<string, ValidatedAllowedHomeAssistantService>(StringComparer.OrdinalIgnoreCase);

        foreach (var service in services)
        {
            ArgumentNullException.ThrowIfNull(service);

            if (string.IsNullOrWhiteSpace(service.Domain))
                throw new ArgumentException("Allowed service domain is required.", nameof(services));

            if (string.IsNullOrWhiteSpace(service.Service))
                throw new ArgumentException("Allowed service name is required.", nameof(services));

            var domain = service.Domain.Trim();
            var name = service.Service.Trim();
            var key = MakeServiceKey(domain, name);
            if (output.ContainsKey(key))
                throw new ArgumentException($"Duplicate allowed service '{domain}/{name}'.", nameof(services));

            var allowedEntityIds = ValidateAllowedEntities(service.AllowedEntityIds);
            output[key] = new ValidatedAllowedHomeAssistantService(domain, name, allowedEntityIds);
        }

        return output;
    }

    internal static string MakeServiceKey(string domain, string service)
        => $"{domain.Trim()}::{service.Trim()}";

    private static Uri NormalizeApiBaseUri(Uri baseUri)
    {
        var builder = new UriBuilder(baseUri)
        {
            Fragment = string.Empty
        };

        var path = builder.Path ?? string.Empty;
        if (!path.EndsWith("/", StringComparison.Ordinal))
            path += "/";

        if (!path.Equals("/api/", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith("/api/", StringComparison.OrdinalIgnoreCase))
            path += "api/";

        builder.Path = path;
        return builder.Uri;
    }
}
