using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.Standard;

public enum WebSafetyCategory { Allowed, Ad, Tracker, Telemetry, Malware, Phishing, Suspicious, Unknown }

public sealed record WebSafetyRule(string Pattern, WebSafetyCategory Category, string? Reason = null);
public enum WebSafetySignalTarget { HostContains, PathContains, QueryContains, PathAndQueryContains, HostIsRawIp }
public sealed record WebSafetySignal(string Id, WebSafetyCategory Category, WebSafetySignalTarget Target, string Pattern, float Weight, string? Reason = null);
public sealed record WebSafetySignalMatch(string Id, WebSafetyCategory Category, float Weight, string Pattern, WebSafetySignalTarget Target, string? Reason = null);
public sealed record WebSafetyScoreReport(float RawScore, float Score, IReadOnlyList<WebSafetySignalMatch> Matches);

public sealed record WebSafetyPolicyOptions
{
    public IReadOnlyList<string> AllowedHosts { get; init; } = [];
    public IReadOnlyList<string> AllowedDestinations { get; init; } = [];
    public IReadOnlyList<WebSafetyRule> BlockRules { get; init; } = [];
    public IReadOnlyList<WebSafetySignal> SuspicionSignals { get; init; } = HttpWebSafetyPolicies.DefaultSuspicionSignals;
    public bool BlockSuspiciousByDefault { get; init; } = true;
    public float SuspicionThreshold { get; init; } = 0.7f;
}

public sealed class HttpWebSafetyActuationPolicy : IActuationPolicy
{
    private readonly ValidatedWebSafetyPolicyOptions _options;
    public HttpWebSafetyActuationPolicy(WebSafetyPolicyOptions options) => _options = WebSafetyPolicyValidation.Validate(options);

    public ActuationPolicyDecision Evaluate(AiCtx ctx, IActuationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var destination = HttpDestination.TryFrom(command);
        if (destination is null) return ActuationPolicyDecision.Allow();
        if (MatchesHost(_options.AllowedHosts, destination.Host)) return ActuationPolicyDecision.Allow();
        if (MatchesDestination(_options.AllowedDestinations, destination.Host, destination.Path)) return ActuationPolicyDecision.Allow();

        foreach (var rule in _options.BlockRules)
            if (rule.Matches(destination.Host, destination.PathAndQuery))
                return ActuationPolicyDecision.Deny($"HTTP request denied by web safety policy: host '{destination.Host}' matched {rule.Category} rule '{rule.Pattern}'.");

        var report = ScoreSuspicion(destination.Uri, _options.SuspicionSignals);
        if (_options.BlockSuspiciousByDefault && report.Score >= _options.SuspicionThreshold)
            return ActuationPolicyDecision.Deny($"HTTP request denied by web safety policy: destination host '{destination.Host}' path '{destination.Path}' scored {report.Score:0.00} suspicious, threshold {_options.SuspicionThreshold:0.00}. Signals: {string.Join(",", report.Matches.Select(static m => m.Id))}.");

        return ActuationPolicyDecision.Allow();
    }

    public static WebSafetyScoreReport ScoreSuspicion(Uri uri, IReadOnlyList<WebSafetySignal> signals)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(signals);
        var score = 0f;
        var matches = new List<WebSafetySignalMatch>();
        foreach (var signal in signals)
        {
            var matched = signal.Target switch
            {
                WebSafetySignalTarget.HostContains => uri.Host.Contains(signal.Pattern, StringComparison.OrdinalIgnoreCase),
                WebSafetySignalTarget.PathContains => uri.AbsolutePath.Contains(signal.Pattern, StringComparison.OrdinalIgnoreCase),
                WebSafetySignalTarget.QueryContains => uri.Query.Contains(signal.Pattern, StringComparison.OrdinalIgnoreCase),
                WebSafetySignalTarget.PathAndQueryContains => uri.PathAndQuery.Contains(signal.Pattern, StringComparison.OrdinalIgnoreCase),
                WebSafetySignalTarget.HostIsRawIp => uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6,
                _ => false
            };
            if (!matched) continue;
            score += signal.Weight;
            matches.Add(new WebSafetySignalMatch(signal.Id, signal.Category, signal.Weight, signal.Pattern, signal.Target, signal.Reason));
        }
        return new WebSafetyScoreReport(score, Math.Clamp(score, 0f, 1f), matches);
    }

    private static bool MatchesHost(IReadOnlyList<string> allowedHosts, string host) => allowedHosts.Any(allowed => IsHostMatch(allowed, host));
    private static bool MatchesDestination(IReadOnlyList<ValidatedAllowedDestination> allowedDestinations, string host, string path)
        => allowedDestinations.Any(allowed => IsHostMatch(allowed.HostPattern, host) && (allowed.PathPrefix is null || path.StartsWith(allowed.PathPrefix, StringComparison.OrdinalIgnoreCase)));
    internal static bool IsHostMatch(string pattern, string host) => pattern.StartsWith(".", StringComparison.Ordinal)
        ? string.Equals(host, pattern[1..], StringComparison.OrdinalIgnoreCase) || host.EndsWith(pattern, StringComparison.OrdinalIgnoreCase)
        : string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase);

    private sealed record HttpDestination(string Host, string PathAndQuery, string Path, Uri Uri)
    {
        public static HttpDestination? TryFrom(IActuationCommand command) => command switch
        {
            HttpGetTextCommand get => FromParts(get.Endpoint, get.Path, get.Query),
            HttpPostJsonCommand postJson => FromParts(postJson.Endpoint, postJson.Path, postJson.Query),
            HttpPostTextCommand postText => FromParts(postText.Endpoint, postText.Path, postText.Query),
            _ => null
        };

        private static HttpDestination FromParts(string endpoint, string path, IReadOnlyDictionary<string, string>? query)
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var absolute) && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
                return new HttpDestination(absolute.Host.ToLowerInvariant(), absolute.PathAndQuery, absolute.AbsolutePath, absolute);
            var pathBuilder = string.IsNullOrWhiteSpace(path) ? "/" : path;
            if (!pathBuilder.StartsWith("/", StringComparison.Ordinal)) pathBuilder = "/" + pathBuilder;
            if (query is { Count: > 0 })
                pathBuilder = $"{pathBuilder}?{string.Join("&", query.Select(static kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"))}";
            var normalizedHost = endpoint.ToLowerInvariant();
            var uri = new Uri($"https://{normalizedHost}{pathBuilder}", UriKind.Absolute);
            return new HttpDestination(normalizedHost, pathBuilder, uri.AbsolutePath, uri);
        }
    }
}

public static class HttpWebSafetyPolicies
{
    public static IReadOnlyList<WebSafetySignal> DefaultSuspicionSignals { get; } =
    [
        new("host.ads", WebSafetyCategory.Ad, WebSafetySignalTarget.HostContains, "ads", 0.35f),
        new("host.raw_ip", WebSafetyCategory.Suspicious, WebSafetySignalTarget.HostIsRawIp, "*", 0.80f, "Raw IP HTTP destination."),
        new("host.tracker", WebSafetyCategory.Tracker, WebSafetySignalTarget.HostContains, "tracker", 0.35f),
        new("host.analytics", WebSafetyCategory.Tracker, WebSafetySignalTarget.HostContains, "analytics", 0.35f),
        new("path.collect", WebSafetyCategory.Telemetry, WebSafetySignalTarget.PathAndQueryContains, "/collect", 0.25f),
        new("path.beacon", WebSafetyCategory.Telemetry, WebSafetySignalTarget.PathAndQueryContains, "/beacon", 0.25f),
        new("query.utm", WebSafetyCategory.Telemetry, WebSafetySignalTarget.PathAndQueryContains, "utm_", 0.25f),
        new("path.pixel", WebSafetyCategory.Tracker, WebSafetySignalTarget.PathAndQueryContains, "pixel", 0.40f)
    ];
    public static WebSafetyPolicyOptions Defaults(IReadOnlyList<string>? allowedHosts = null) => new()
    {
        AllowedHosts = allowedHosts ?? [],
        BlockRules = [new(".doubleclick.net", WebSafetyCategory.Ad, "Known ad domain"), new(".googlesyndication.com", WebSafetyCategory.Ad, "Known ad domain"), new(".google-analytics.com", WebSafetyCategory.Tracker, "Known tracker domain"), new("hostpath:facebook.com/tr", WebSafetyCategory.Tracker, "Known tracker path"), new("path:/collect", WebSafetyCategory.Tracker, "Common telemetry path"), new("path:/ads", WebSafetyCategory.Ad, "Common ad path"), new("path:/malware-test", WebSafetyCategory.Malware, "Test malware rule")]
    };
    public static IActuationPolicy Default(IReadOnlyList<string>? allowedHosts = null) => new HttpWebSafetyActuationPolicy(Defaults(allowedHosts));
}

internal sealed record ValidatedWebSafetyPolicyOptions(IReadOnlyList<string> AllowedHosts, IReadOnlyList<ValidatedAllowedDestination> AllowedDestinations, IReadOnlyList<ValidatedWebSafetyRule> BlockRules, IReadOnlyList<WebSafetySignal> SuspicionSignals, bool BlockSuspiciousByDefault, float SuspicionThreshold);
internal sealed record ValidatedAllowedDestination(string HostPattern, string? PathPrefix);
internal sealed record ValidatedWebSafetyRule(string Pattern, WebSafetyCategory Category, string? Reason)
{
    public bool Matches(string host, string pathAndQuery)
    {
        if (Pattern.StartsWith("hostpath:", StringComparison.OrdinalIgnoreCase))
        {
            var patternValue = Pattern[9..];
            var slashIndex = patternValue.IndexOf('/');
            if (slashIndex <= 0) return false;
            var hostPattern = patternValue[..slashIndex];
            var pathPrefix = patternValue[slashIndex..];
            return HttpWebSafetyActuationPolicy.IsHostMatch(hostPattern, host)
                && pathAndQuery.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase);
        }
        if (Pattern.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
        {
            var patternValue = Pattern[5..];
            return pathAndQuery.Contains(patternValue, StringComparison.OrdinalIgnoreCase);
        }
        return Pattern.StartsWith(".", StringComparison.Ordinal) ? host.EndsWith(Pattern, StringComparison.OrdinalIgnoreCase) : string.Equals(host, Pattern, StringComparison.OrdinalIgnoreCase);
    }
}
internal static class WebSafetyPolicyValidation
{
    public static ValidatedWebSafetyPolicyOptions Validate(WebSafetyPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var allowedHosts = new List<string>(); var seenAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in options.AllowedHosts) { var normalized = NormalizeHostPattern(host, nameof(options.AllowedHosts)); if (seenAllowed.Add(normalized)) allowedHosts.Add(normalized); }
        var allowedDestinations = new List<ValidatedAllowedDestination>(); var seenDestination = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var destination in options.AllowedDestinations)
        {
            var normalized = NormalizeAllowedDestination(destination, nameof(options.AllowedDestinations));
            var key = normalized.HostPattern + normalized.PathPrefix;
            if (seenDestination.Add(key)) allowedDestinations.Add(normalized);
        }
        var rules = new List<ValidatedWebSafetyRule>(); var seenRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in options.BlockRules)
        {
            if (rule is null) throw new ArgumentException("Block rule entry cannot be null.", nameof(options.BlockRules));
            if (rule.Category == WebSafetyCategory.Allowed) throw new ArgumentException("Block rule category cannot be Allowed.", nameof(options.BlockRules));
            if (string.IsNullOrWhiteSpace(rule.Pattern)) throw new ArgumentException("Block rule pattern is required.", nameof(options.BlockRules));
            var normalizedPattern = NormalizePattern(rule.Pattern);
            if (!seenRules.Add(normalizedPattern)) throw new ArgumentException($"Duplicate web safety rule pattern '{normalizedPattern}'.", nameof(options.BlockRules));
            rules.Add(new ValidatedWebSafetyRule(normalizedPattern, rule.Category, rule.Reason));
        }
        var signals = new List<WebSafetySignal>(); var seenSignals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var signal in options.SuspicionSignals)
        {
            if (signal is null) throw new ArgumentException("Suspicion signal entry cannot be null.", nameof(options.SuspicionSignals));
            if (string.IsNullOrWhiteSpace(signal.Id)) throw new ArgumentException("Suspicion signal Id is required.", nameof(options.SuspicionSignals));
            if (string.IsNullOrWhiteSpace(signal.Pattern)) throw new ArgumentException("Suspicion signal Pattern is required.", nameof(options.SuspicionSignals));
            if (signal.Weight <= 0f) throw new ArgumentException("Suspicion signal Weight must be > 0.", nameof(options.SuspicionSignals));
            var normalized = signal with { Id = signal.Id.Trim(), Pattern = signal.Pattern.Trim() };
            if (!seenSignals.Add(normalized.Id)) throw new ArgumentException($"Duplicate suspicion signal Id '{normalized.Id}'.", nameof(options.SuspicionSignals));
            signals.Add(normalized);
        }
        return new ValidatedWebSafetyPolicyOptions(allowedHosts, allowedDestinations, rules, signals, options.BlockSuspiciousByDefault, Math.Clamp(options.SuspicionThreshold, 0f, 1f));
    }
    private static string NormalizePattern(string pattern)
    {
        var value = pattern.Trim();
        if (value.StartsWith("path:", StringComparison.OrdinalIgnoreCase)) return "path:" + value[5..];
        if (value.StartsWith("hostpath:", StringComparison.OrdinalIgnoreCase))
        {
            var ruleValue = value[9..];
            var slashIndex = ruleValue.IndexOf('/');
            if (slashIndex <= 0 || slashIndex == ruleValue.Length - 1) throw new ArgumentException($"Host+path rule pattern '{pattern}' must be in the form hostpath:<host>/<path>.", "BlockRules");
            var host = NormalizeHostPattern(ruleValue[..slashIndex], "BlockRules");
            var pathPrefix = NormalizePathPrefix(ruleValue[slashIndex..], "BlockRules");
            return $"hostpath:{host}{pathPrefix}";
        }
        return NormalizeHostPattern(value, "BlockRules");
    }
    private static ValidatedAllowedDestination NormalizeAllowedDestination(string destination, string paramName)
    {
        if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("Destination value is required.", paramName);
        var value = destination.Trim().ToLowerInvariant();
        if (value.Contains("://", StringComparison.Ordinal)) throw new ArgumentException($"Destination pattern '{destination}' cannot include URI scheme.", paramName);
        if (value.Contains("?")) throw new ArgumentException($"Destination pattern '{destination}' cannot include query string.", paramName);
        var slashIndex = value.IndexOf('/');
        if (slashIndex < 0) return new ValidatedAllowedDestination(NormalizeHostPattern(value, paramName), null);
        var host = NormalizeHostPattern(value[..slashIndex], paramName);
        var pathPrefix = NormalizePathPrefix(value[slashIndex..], paramName);
        return new ValidatedAllowedDestination(host, pathPrefix);
    }
    private static string NormalizePathPrefix(string pathPrefix, string paramName)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix) || !pathPrefix.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("Path prefix must start with '/'.", paramName);
        if (pathPrefix.Contains("?")) throw new ArgumentException("Path prefix cannot include query string.", paramName);
        return pathPrefix;
    }
    private static string NormalizeHostPattern(string host, string paramName)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host value is required.", paramName);
        var value = host.Trim().ToLowerInvariant();
        if (value.Contains("/")) throw new ArgumentException($"Host pattern '{host}' cannot contain '/'.", paramName);
        if (value.Contains("://", StringComparison.Ordinal)) throw new ArgumentException($"Host pattern '{host}' cannot include URI scheme.", paramName);
        var core = value.StartsWith(".", StringComparison.Ordinal) ? value[1..] : value;
        if (core.Length == 0 || core.Contains(' ')) throw new ArgumentException($"Host pattern '{host}' is invalid.", paramName);
        return value;
    }
}
