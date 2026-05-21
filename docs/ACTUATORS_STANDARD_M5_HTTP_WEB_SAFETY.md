# Dominatus.Actuators.Standard M5 — HTTP Web Safety Policy

## Purpose
`HttpWebSafetyActuationPolicy` adds deterministic, reusable actuation-time safety checks for Standard HTTP commands.

## Safe-by-default doctrine
- Known ad/tracker/malware/suspicious destinations are denied.
- Explicit `AllowedHosts` entries override deny rules.
- Explicit `AllowedDestinations` entries support host-only or host+path-prefix allowlisting.
- Unknown ordinary destinations are allowed unless explicit rule/suspicion denies.

## Scope and limitations
This is a small deterministic guardrail layer, **not** comprehensive adblocking or malware protection.
No DNS checks, remote feeds, subscriptions, crawlers, or external blocklists are used.

## API
- `WebSafetyCategory`
- `WebSafetyRule`
- `WebSafetyPolicyOptions`
- `WebSafetySignalTarget`
- `WebSafetySignal`
- `WebSafetySignalMatch`
- `WebSafetyScoreReport`
- `HttpWebSafetyActuationPolicy : IActuationPolicy`
- `HttpWebSafetyPolicies.Defaults(...)` and `HttpWebSafetyPolicies.Default(...)`

## Pattern semantics
- Exact host: `ads.example.com`
- Host suffix (leading dot): `.doubleclick.net`
- Path/query substring: `path:/collect`
- Host+path prefix: `hostpath:facebook.com/tr`

`path:` is path/query-only.  
`hostpath:` requires both host match (exact or leading-dot suffix semantics) and path prefix match.

## Default baseline rules
Tiny examples only:
- `.doubleclick.net` (Ad)
- `.googlesyndication.com` (Ad)
- `.google-analytics.com` (Tracker)
- `hostpath:facebook.com/tr` (Tracker)
- `path:/collect` (Tracker)
- `path:/ads` (Ad)
- `path:/malware-test` (Malware)

## Whitelist behavior
`AllowedHosts` supports exact host or leading-dot suffix. Entries are normalized to lowercase.
Leading-dot suffix entries match the root host and subdomains (for example, `.example.com` matches `example.com` and `ads.example.com`).
Whitelist is evaluated first and wins over block rules and suspicion scoring.

`AllowedDestinations` supports host-only and host+path-prefix entries:
- `api.partner.com` (host-only)
- `.partner.com` (host suffix)
- `api.partner.com/v2/data` (host + `/v2/data` prefix only)
- `.partner.com/v2/data` (suffix host + `/v2/data` prefix only)

Validation rejects scheme/query and requires path prefixes to start with `/`.

## Suspicion scoring
Weighted deterministic signals (configured order) with raw sum and clamped score (`0..1`) at default threshold `0.7`.
Each matching signal contributes weight and is captured in `WebSafetyScoreReport.Matches`.
`WebSafetyScoreReport.RawScore` captures the unclamped sum for audit/tuning.

Default signal baseline:
- `host.ads` host contains `ads` (+0.35)
- `host.raw_ip` host is raw IPv4/IPv6 (+0.80)
- `host.tracker` host contains `tracker` (+0.35)
- `host.analytics` host contains `analytics` (+0.35)
- `path.collect` path/query contains `/collect` (+0.25)
- `path.beacon` path/query contains `/beacon` (+0.25)
- `query.utm` path/query contains `utm_` (+0.25)
- `path.pixel` path/query contains `pixel` (+0.40)

If `score >= SuspicionThreshold` and `BlockSuspiciousByDefault = true`, request is denied.
Whitelist is evaluated before scoring. Explicit block rules are evaluated before scoring.
Suspicion deny reasons include matched signal IDs and destination host/path (not full query values).

## M5.3 hardening note
M5.3 fixed an ambiguous host+path rule bug and added raw-IP/path-scoped allowlist hardening.

## Registration
Recommended explicit registration on host policy chain:

```csharp
var host = new ActuatorHost();
host.RegisterStandardHttpActuators(httpOptions);
host.AddPolicy(HttpWebSafetyPolicies.Default());
```

For ad/analytics-required destinations, explicitly whitelist those hosts.

## Composition
This policy composes with existing Core `IActuationPolicy` behavior and can be combined with `ActuationPolicies.AllOf(...)`.

## Non-goals
No browser/proxy integration, DNS lookups, remote threat intelligence, or runtime network policy fetching.
This is agent web safety guardrailing, not consumer adblock completeness.
