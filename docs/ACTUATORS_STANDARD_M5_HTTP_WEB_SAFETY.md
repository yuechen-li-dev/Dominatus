# Dominatus.Actuators.Standard M5 — HTTP Web Safety Policy

## Purpose
`HttpWebSafetyActuationPolicy` adds deterministic, reusable actuation-time safety checks for Standard HTTP commands.

## Safe-by-default doctrine
- Known ad/tracker/malware/suspicious destinations are denied.
- Explicit `AllowedHosts` entries override deny rules.
- Unknown ordinary destinations are allowed unless explicit rule/suspicion denies.

## Scope and limitations
This is a small deterministic guardrail layer, **not** comprehensive adblocking or malware protection.
No DNS checks, remote feeds, subscriptions, crawlers, or external blocklists are used.

## API
- `WebSafetyCategory`
- `WebSafetyRule`
- `WebSafetyPolicyOptions`
- `HttpWebSafetyActuationPolicy : IActuationPolicy`
- `HttpWebSafetyPolicies.Defaults(...)` and `HttpWebSafetyPolicies.Default(...)`

## Pattern semantics
- Exact host: `ads.example.com`
- Host suffix (leading dot): `.doubleclick.net`
- Path/query substring: `path:/collect`
- Host+path substring fallback: `path:facebook.com/tr`

## Default baseline rules
Tiny examples only:
- `.doubleclick.net` (Ad)
- `.googlesyndication.com` (Ad)
- `.google-analytics.com` (Tracker)
- `path:facebook.com/tr` (Tracker)
- `path:/collect` (Tracker)
- `path:/ads` (Ad)
- `path:/malware-test` (Malware)

## Whitelist behavior
`AllowedHosts` supports exact host or leading-dot suffix. Entries are normalized to lowercase.
Leading-dot suffix entries match the root host and subdomains (for example, `.example.com` matches `example.com` and `ads.example.com`).
Whitelist is evaluated first and wins over block rules and suspicion scoring.

## Suspicion scoring
Deterministic heuristic (clamped 0..1) with default threshold `0.7`:
- host contains `ads` +0.35
- host contains `tracker` +0.35
- host contains `analytics` +0.35
- path/query contains `/collect` +0.25
- path/query contains `/beacon` +0.25
- path/query contains `utm_` +0.25
- path/query contains `pixel` +0.40

If `score >= SuspicionThreshold` and `BlockSuspiciousByDefault = true`, request is denied.

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
