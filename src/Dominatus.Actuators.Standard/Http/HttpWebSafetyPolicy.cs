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
    public IReadOnlyList<WebSafetyCategory> BlockCategories { get; init; } = HttpWebSafetyPolicies.DefaultBlockCategories;
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
        var blockedCategoryMatches = report.Matches.Where(m => _options.BlockCategories.Contains(m.Category)).ToArray();
        if (blockedCategoryMatches.Length > 0)
            return ActuationPolicyDecision.Deny($"HTTP request denied by web safety policy: destination host '{destination.Host}' path '{destination.Path}' matched blocked categories [{string.Join(",", blockedCategoryMatches.Select(static m => m.Category).Distinct())}] via signals [{string.Join(",", blockedCategoryMatches.Select(static m => m.Id))}].");
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
    public static IReadOnlyList<WebSafetyCategory> DefaultBlockCategories { get; } = [WebSafetyCategory.Malware, WebSafetyCategory.Phishing];
    public static IReadOnlyList<WebSafetySignal> DefaultSuspicionSignals { get; } =
    [
        new("host.ads", WebSafetyCategory.Ad, WebSafetySignalTarget.HostContains, "ads", 0.35f),
        new("host.adserver", WebSafetyCategory.Ad, WebSafetySignalTarget.HostContains, "adserver", 0.40f),
        new("host.adsystem", WebSafetyCategory.Ad, WebSafetySignalTarget.HostContains, "adsystem", 0.40f),
        new("host.adcdn", WebSafetyCategory.Ad, WebSafetySignalTarget.HostContains, "adcdn", 0.40f),
        new("host.adnxs", WebSafetyCategory.Ad, WebSafetySignalTarget.HostContains, "adnxs", 0.90f),
        new("host.casalemedia", WebSafetyCategory.Ad, WebSafetySignalTarget.HostContains, "casalemedia", 0.90f),
        new("host.rubiconproject", WebSafetyCategory.Ad, WebSafetySignalTarget.HostContains, "rubiconproject", 0.90f),
        new("host.openx", WebSafetyCategory.Ad, WebSafetySignalTarget.HostContains, "openx", 0.85f),
        new("host.pubmatic", WebSafetyCategory.Ad, WebSafetySignalTarget.HostContains, "pubmatic", 0.90f),
        new("host.criteo", WebSafetyCategory.Ad, WebSafetySignalTarget.HostContains, "criteo", 0.90f),
        new("host.taboola", WebSafetyCategory.Ad, WebSafetySignalTarget.HostContains, "taboola", 0.90f),
        new("host.outbrain", WebSafetyCategory.Ad, WebSafetySignalTarget.HostContains, "outbrain", 0.90f),
        new("host.raw_ip", WebSafetyCategory.Suspicious, WebSafetySignalTarget.HostIsRawIp, "*", 0.80f, "Raw IP HTTP destination."),
        new("host.tracker", WebSafetyCategory.Tracker, WebSafetySignalTarget.HostContains, "tracker", 0.35f),
        new("host.track", WebSafetyCategory.Tracker, WebSafetySignalTarget.HostContains, "track", 0.25f),
        new("host.pixel", WebSafetyCategory.Tracker, WebSafetySignalTarget.HostContains, "pixel", 0.30f),
        new("host.beacon", WebSafetyCategory.Tracker, WebSafetySignalTarget.HostContains, "beacon", 0.30f),
        new("host.hit", WebSafetyCategory.Tracker, WebSafetySignalTarget.HostContains, "hit", 0.20f),
        new("host.analytics", WebSafetyCategory.Tracker, WebSafetySignalTarget.HostContains, "analytics", 0.35f),
        new("host.telemetry", WebSafetyCategory.Telemetry, WebSafetySignalTarget.HostContains, "telemetry", 0.35f),
        new("host.metrics", WebSafetyCategory.Telemetry, WebSafetySignalTarget.HostContains, "metrics", 0.30f),
        new("host.stat", WebSafetyCategory.Telemetry, WebSafetySignalTarget.HostContains, "stat", 0.25f),
        new("host.stats", WebSafetyCategory.Telemetry, WebSafetySignalTarget.HostContains, "stats", 0.30f),
        new("host.log", WebSafetyCategory.Telemetry, WebSafetySignalTarget.HostContains, "log", 0.20f),
        new("host.logs", WebSafetyCategory.Telemetry, WebSafetySignalTarget.HostContains, "logs", 0.20f),
        new("host.event", WebSafetyCategory.Telemetry, WebSafetySignalTarget.HostContains, "event", 0.25f),
        new("host.events", WebSafetyCategory.Telemetry, WebSafetySignalTarget.HostContains, "events", 0.30f),
        new("host.ping", WebSafetyCategory.Telemetry, WebSafetySignalTarget.HostContains, "ping", 0.25f),
        new("host.ingest", WebSafetyCategory.Telemetry, WebSafetySignalTarget.HostContains, "ingest", 0.35f),
        new("host.intake", WebSafetyCategory.Telemetry, WebSafetySignalTarget.HostContains, "intake", 0.35f),
        new("host.sink", WebSafetyCategory.Telemetry, WebSafetySignalTarget.HostContains, "sink", 0.35f),
        new("host.report", WebSafetyCategory.Telemetry, WebSafetySignalTarget.HostContains, "report", 0.25f),
        new("host.data_low", WebSafetyCategory.Telemetry, WebSafetySignalTarget.HostContains, "data", 0.15f),
        new("host.hotjar", WebSafetyCategory.Tracker, WebSafetySignalTarget.HostContains, "hotjar", 0.90f),
        new("host.mixpanel", WebSafetyCategory.Tracker, WebSafetySignalTarget.HostContains, "mixpanel", 0.90f),
        new("host.amplitude", WebSafetyCategory.Tracker, WebSafetySignalTarget.HostContains, "amplitude", 0.90f),
        new("host.segment", WebSafetyCategory.Tracker, WebSafetySignalTarget.HostContains, "segment", 0.85f),
        new("host.heapanalytics", WebSafetyCategory.Tracker, WebSafetySignalTarget.HostContains, "heapanalytics", 0.90f),
        new("host.fullstory", WebSafetyCategory.Tracker, WebSafetySignalTarget.HostContains, "fullstory", 0.90f),
        new("host.logrocket", WebSafetyCategory.Tracker, WebSafetySignalTarget.HostContains, "logrocket", 0.90f),
        new("host.datadog_rum", WebSafetyCategory.Tracker, WebSafetySignalTarget.HostContains, "datadog", 0.80f),
        new("host.malware", WebSafetyCategory.Malware, WebSafetySignalTarget.HostContains, "malware", 0.95f),
        new("host.phish", WebSafetyCategory.Phishing, WebSafetySignalTarget.HostContains, "phish", 0.95f),
        new("path.collect", WebSafetyCategory.Telemetry, WebSafetySignalTarget.PathAndQueryContains, "/collect", 0.25f),
        new("path.beacon", WebSafetyCategory.Telemetry, WebSafetySignalTarget.PathAndQueryContains, "/beacon", 0.25f),
        new("path.track", WebSafetyCategory.Tracker, WebSafetySignalTarget.PathAndQueryContains, "/track", 0.30f),
        new("path.tracking", WebSafetyCategory.Tracker, WebSafetySignalTarget.PathAndQueryContains, "/tracking", 0.30f),
        new("path.hit", WebSafetyCategory.Tracker, WebSafetySignalTarget.PathAndQueryContains, "/hit", 0.25f),
        new("path.ping", WebSafetyCategory.Telemetry, WebSafetySignalTarget.PathAndQueryContains, "/ping", 0.25f),
        new("path.log", WebSafetyCategory.Telemetry, WebSafetySignalTarget.PathAndQueryContains, "/log", 0.25f),
        new("path.event", WebSafetyCategory.Telemetry, WebSafetySignalTarget.PathAndQueryContains, "/event", 0.30f),
        new("path.events", WebSafetyCategory.Telemetry, WebSafetySignalTarget.PathAndQueryContains, "/events", 0.30f),
        new("path.metrics", WebSafetyCategory.Telemetry, WebSafetySignalTarget.PathAndQueryContains, "/metrics", 0.35f),
        new("path.telemetry", WebSafetyCategory.Telemetry, WebSafetySignalTarget.PathAndQueryContains, "/telemetry", 0.35f),
        new("path.ingest", WebSafetyCategory.Telemetry, WebSafetySignalTarget.PathAndQueryContains, "/ingest", 0.35f),
        new("path.intake", WebSafetyCategory.Telemetry, WebSafetySignalTarget.PathAndQueryContains, "/intake", 0.35f),
        new("path.impression", WebSafetyCategory.Ad, WebSafetySignalTarget.PathAndQueryContains, "/impression", 0.40f),
        new("path.click", WebSafetyCategory.Tracker, WebSafetySignalTarget.PathAndQueryContains, "/click", 0.20f),
        new("path.conversion", WebSafetyCategory.Tracker, WebSafetySignalTarget.PathAndQueryContains, "/conversion", 0.45f),
        new("path.report", WebSafetyCategory.Telemetry, WebSafetySignalTarget.PathAndQueryContains, "/report", 0.25f),
        new("path.identify", WebSafetyCategory.Tracker, WebSafetySignalTarget.PathAndQueryContains, "/identify", 0.50f),
        new("path.page", WebSafetyCategory.Telemetry, WebSafetySignalTarget.PathAndQueryContains, "/page", 0.20f),
        new("path.alias", WebSafetyCategory.Tracker, WebSafetySignalTarget.PathAndQueryContains, "/alias", 0.45f),
        new("path.exfil", WebSafetyCategory.Malware, WebSafetySignalTarget.PathAndQueryContains, "/exfil", 0.95f),
        new("path.dump", WebSafetyCategory.Suspicious, WebSafetySignalTarget.PathAndQueryContains, "/dump", 0.60f),
        new("query.utm", WebSafetyCategory.Telemetry, WebSafetySignalTarget.PathAndQueryContains, "utm_", 0.25f),
        new("query.cid", WebSafetyCategory.Tracker, WebSafetySignalTarget.QueryContains, "cid=", 0.30f),
        new("query.uid", WebSafetyCategory.Tracker, WebSafetySignalTarget.QueryContains, "uid=", 0.30f),
        new("query.sid", WebSafetyCategory.Tracker, WebSafetySignalTarget.QueryContains, "sid=", 0.30f),
        new("query.tid", WebSafetyCategory.Tracker, WebSafetySignalTarget.QueryContains, "tid=", 0.30f),
        new("query.fbclid", WebSafetyCategory.Tracker, WebSafetySignalTarget.QueryContains, "fbclid=", 0.55f),
        new("query.gclid", WebSafetyCategory.Tracker, WebSafetySignalTarget.QueryContains, "gclid=", 0.55f),
        new("query.msclkid", WebSafetyCategory.Tracker, WebSafetySignalTarget.QueryContains, "msclkid=", 0.55f),
        new("query.ttclid", WebSafetyCategory.Tracker, WebSafetySignalTarget.QueryContains, "ttclid=", 0.55f),
        new("query.mc_eid", WebSafetyCategory.Tracker, WebSafetySignalTarget.QueryContains, "mc_eid=", 0.55f),
        new("query.igshid", WebSafetyCategory.Tracker, WebSafetySignalTarget.QueryContains, "igshid=", 0.55f),
        new("query.data", WebSafetyCategory.Telemetry, WebSafetySignalTarget.QueryContains, "data=", 0.20f),
        new("query.payload", WebSafetyCategory.Suspicious, WebSafetySignalTarget.QueryContains, "payload=", 0.60f),
        new("query.exfil", WebSafetyCategory.Malware, WebSafetySignalTarget.QueryContains, "exfil=", 0.95f),
        new("path.pixel", WebSafetyCategory.Tracker, WebSafetySignalTarget.PathAndQueryContains, "pixel", 0.40f)
    ];
    public static WebSafetyPolicyOptions Defaults(IReadOnlyList<string>? allowedHosts = null) => new()
    {
        AllowedHosts = allowedHosts ?? [],
        BlockRules = [new(".doubleclick.net", WebSafetyCategory.Ad, "Known ad domain"), new(".googlesyndication.com", WebSafetyCategory.Ad, "Known ad domain"), new(".google-analytics.com", WebSafetyCategory.Tracker, "Known tracker domain"), new("hostpath:facebook.com/tr", WebSafetyCategory.Tracker, "Known tracker path"), new("path:/collect", WebSafetyCategory.Tracker, "Common telemetry path"), new("path:/ads", WebSafetyCategory.Ad, "Common ad path"), new("path:/malware-test", WebSafetyCategory.Malware, "Test malware rule")]
    };
    public static IActuationPolicy Default(IReadOnlyList<string>? allowedHosts = null) => new HttpWebSafetyActuationPolicy(Defaults(allowedHosts));
}

internal sealed record ValidatedWebSafetyPolicyOptions(IReadOnlyList<string> AllowedHosts, IReadOnlyList<ValidatedAllowedDestination> AllowedDestinations, IReadOnlyList<ValidatedWebSafetyRule> BlockRules, IReadOnlyList<WebSafetySignal> SuspicionSignals, IReadOnlySet<WebSafetyCategory> BlockCategories, bool BlockSuspiciousByDefault, float SuspicionThreshold);
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
        var blockCategories = new HashSet<WebSafetyCategory>(options.BlockCategories.Where(static c => c != WebSafetyCategory.Allowed));
        return new ValidatedWebSafetyPolicyOptions(allowedHosts, allowedDestinations, rules, signals, blockCategories, options.BlockSuspiciousByDefault, Math.Clamp(options.SuspicionThreshold, 0f, 1f));
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
