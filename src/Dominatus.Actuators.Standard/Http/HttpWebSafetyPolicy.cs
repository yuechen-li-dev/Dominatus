using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.Standard;

public enum WebSafetyCategory
{
    Allowed,
    Ad,
    Tracker,
    Telemetry,
    Malware,
    Phishing,
    Suspicious,
    Unknown
}

public sealed record WebSafetyRule(
    string Pattern,
    WebSafetyCategory Category,
    string? Reason = null);

public sealed record WebSafetyPolicyOptions
{
    public IReadOnlyList<string> AllowedHosts { get; init; } = [];
    public IReadOnlyList<WebSafetyRule> BlockRules { get; init; } = [];
    public bool BlockSuspiciousByDefault { get; init; } = true;
    public float SuspicionThreshold { get; init; } = 0.7f;
}

public sealed class HttpWebSafetyActuationPolicy : IActuationPolicy
{
    private readonly ValidatedWebSafetyPolicyOptions _options;

    public HttpWebSafetyActuationPolicy(WebSafetyPolicyOptions options)
        => _options = WebSafetyPolicyValidation.Validate(options);

    public ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var destination = HttpDestination.TryFrom(command);
        if (destination is null)
            return ActuationPolicyDecision.Allow();

        if (MatchesHost(_options.AllowedHosts, destination.Host))
            return ActuationPolicyDecision.Allow();

        foreach (var rule in _options.BlockRules)
        {
            if (!rule.Matches(destination.Host, destination.PathAndQuery))
                continue;

            return ActuationPolicyDecision.Deny(
                $"HTTP request denied by web safety policy: host '{destination.Host}' matched {rule.Category} rule '{rule.Pattern}'.");
        }

        var score = ScoreSuspicion(destination.Host, destination.PathAndQuery);
        if (_options.BlockSuspiciousByDefault && score >= _options.SuspicionThreshold)
        {
            return ActuationPolicyDecision.Deny(
                $"HTTP request denied by web safety policy: destination scored {score:0.00} suspicious, threshold {_options.SuspicionThreshold:0.00}.");
        }

        return ActuationPolicyDecision.Allow();
    }

    private static bool MatchesHost(IReadOnlyList<string> allowedHosts, string host)
    {
        foreach (var allowed in allowedHosts)
        {
            if (IsHostMatch(allowed, host))
                return true;
        }

        return false;
    }

    private static bool IsHostMatch(string pattern, string host)
    {
        if (pattern.StartsWith(".", StringComparison.Ordinal))
            return host.EndsWith(pattern, StringComparison.OrdinalIgnoreCase);

        return string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase);
    }

    private static float ScoreSuspicion(string host, string pathAndQuery)
    {
        var score = 0f;
        if (host.Contains("ads", StringComparison.OrdinalIgnoreCase)) score += 0.35f;
        if (host.Contains("tracker", StringComparison.OrdinalIgnoreCase)) score += 0.35f;
        if (host.Contains("analytics", StringComparison.OrdinalIgnoreCase)) score += 0.35f;
        if (pathAndQuery.Contains("/collect", StringComparison.OrdinalIgnoreCase)) score += 0.25f;
        if (pathAndQuery.Contains("/beacon", StringComparison.OrdinalIgnoreCase)) score += 0.25f;
        if (pathAndQuery.Contains("utm_", StringComparison.OrdinalIgnoreCase)) score += 0.25f;
        if (pathAndQuery.Contains("pixel", StringComparison.OrdinalIgnoreCase)) score += 0.40f;

        return Math.Clamp(score, 0f, 1f);
    }

    private sealed record HttpDestination(string Host, string PathAndQuery)
    {
        public static HttpDestination? TryFrom(IActuationCommand command)
            => command switch
            {
                HttpGetTextCommand get => FromParts(get.Endpoint, get.Path, get.Query),
                HttpPostJsonCommand postJson => FromParts(postJson.Endpoint, postJson.Path, postJson.Query),
                HttpPostTextCommand postText => FromParts(postText.Endpoint, postText.Path, postText.Query),
                _ => null
            };

        private static HttpDestination FromParts(string endpoint, string path, IReadOnlyDictionary<string, string>? query)
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var absolute)
                && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            {
                return new HttpDestination(absolute.Host.ToLowerInvariant(), absolute.PathAndQuery);
            }

            var pathBuilder = string.IsNullOrWhiteSpace(path) ? "/" : path;
            if (!pathBuilder.StartsWith("/", StringComparison.Ordinal))
                pathBuilder = "/" + pathBuilder;

            if (query is { Count: > 0 })
            {
                var segments = query.Select(static kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}");
                pathBuilder = $"{pathBuilder}?{string.Join("&", segments)}";
            }

            return new HttpDestination(endpoint.ToLowerInvariant(), pathBuilder);
        }
    }
}

public static class HttpWebSafetyPolicies
{
    public static WebSafetyPolicyOptions Defaults(IReadOnlyList<string>? allowedHosts = null)
        => new()
        {
            AllowedHosts = allowedHosts ?? [],
            BlockRules =
            [
                new WebSafetyRule(".doubleclick.net", WebSafetyCategory.Ad, "Known ad domain"),
                new WebSafetyRule(".googlesyndication.com", WebSafetyCategory.Ad, "Known ad domain"),
                new WebSafetyRule(".google-analytics.com", WebSafetyCategory.Tracker, "Known tracker domain"),
                new WebSafetyRule("path:facebook.com/tr", WebSafetyCategory.Tracker, "Known tracker path"),
                new WebSafetyRule("path:/collect", WebSafetyCategory.Tracker, "Common telemetry path"),
                new WebSafetyRule("path:/ads", WebSafetyCategory.Ad, "Common ad path"),
                new WebSafetyRule("path:/malware-test", WebSafetyCategory.Malware, "Test malware rule")
            ]
        };

    public static IActuationPolicy Default(IReadOnlyList<string>? allowedHosts = null)
        => new HttpWebSafetyActuationPolicy(Defaults(allowedHosts));
}

internal sealed record ValidatedWebSafetyPolicyOptions(
    IReadOnlyList<string> AllowedHosts,
    IReadOnlyList<ValidatedWebSafetyRule> BlockRules,
    bool BlockSuspiciousByDefault,
    float SuspicionThreshold);

internal sealed record ValidatedWebSafetyRule(string Pattern, WebSafetyCategory Category, string? Reason)
{
    public bool Matches(string host, string pathAndQuery)
    {
        if (Pattern.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
        {
            var patternValue = Pattern[5..];
            if (patternValue.Contains("/"))
                return pathAndQuery.Contains(patternValue, StringComparison.OrdinalIgnoreCase);

            return (host + pathAndQuery).Contains(patternValue, StringComparison.OrdinalIgnoreCase);
        }

        if (Pattern.StartsWith(".", StringComparison.Ordinal))
            return host.EndsWith(Pattern, StringComparison.OrdinalIgnoreCase);

        return string.Equals(host, Pattern, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class WebSafetyPolicyValidation
{
    public static ValidatedWebSafetyPolicyOptions Validate(WebSafetyPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var allowedHosts = new List<string>();
        var seenAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in options.AllowedHosts)
        {
            var normalized = NormalizeHostPattern(host, nameof(options.AllowedHosts));
            if (!seenAllowed.Add(normalized))
                continue;
            allowedHosts.Add(normalized);
        }

        var rules = new List<ValidatedWebSafetyRule>();
        var seenRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in options.BlockRules)
        {
            if (rule is null)
                throw new ArgumentException("Block rule entry cannot be null.", nameof(options.BlockRules));

            if (rule.Category == WebSafetyCategory.Allowed)
                throw new ArgumentException("Block rule category cannot be Allowed.", nameof(options.BlockRules));

            if (string.IsNullOrWhiteSpace(rule.Pattern))
                throw new ArgumentException("Block rule pattern is required.", nameof(options.BlockRules));

            var normalizedPattern = NormalizePattern(rule.Pattern);
            if (!seenRules.Add(normalizedPattern))
                throw new ArgumentException($"Duplicate web safety rule pattern '{normalizedPattern}'.", nameof(options.BlockRules));

            rules.Add(new ValidatedWebSafetyRule(normalizedPattern, rule.Category, rule.Reason));
        }

        return new ValidatedWebSafetyPolicyOptions(
            allowedHosts,
            rules,
            options.BlockSuspiciousByDefault,
            Math.Clamp(options.SuspicionThreshold, 0f, 1f));
    }

    private static string NormalizePattern(string pattern)
    {
        var value = pattern.Trim();
        if (value.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
            return "path:" + value[5..];

        return NormalizeHostPattern(value, "BlockRules");
    }

    private static string NormalizeHostPattern(string host, string paramName)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host value is required.", paramName);

        var value = host.Trim().ToLowerInvariant();

        if (value.Contains("/"))
            throw new ArgumentException($"Host pattern '{host}' cannot contain '/'.", paramName);

        if (value.Contains("://", StringComparison.Ordinal))
            throw new ArgumentException($"Host pattern '{host}' cannot include URI scheme.", paramName);

        var core = value.StartsWith(".", StringComparison.Ordinal) ? value[1..] : value;
        if (core.Length == 0 || core.Contains(' '))
            throw new ArgumentException($"Host pattern '{host}' is invalid.", paramName);

        return value;
    }
}
