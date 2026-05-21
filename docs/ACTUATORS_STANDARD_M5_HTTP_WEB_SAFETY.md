# Dominatus.Actuators.Standard M5 — HTTP Web Safety Policy

## Purpose
`HttpWebSafetyActuationPolicy` adds deterministic, reusable actuation-time safety checks for Standard HTTP commands.

## Safe-by-default doctrine
- Known ad/tracker/malware/suspicious destinations are denied.
- Explicit `AllowedHosts` entries override deny rules.
- Explicit `AllowedDestinations` entries support host-only or host+path-prefix allowlisting.
- Unknown ordinary destinations are allowed unless explicit rule/suspicion denies.

## Scope and limitations
This is a deterministic guardrail layer, **not** comprehensive adblocking or malware protection.
No DNS checks, remote feeds, subscriptions, crawlers, or external blocklists are used.
This policy reduces agent web-risk. It is not a complete browser security product.

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

## Default signal library
The signal model is behavioral (weighted vocabulary), not a complete blocklist.

Defaults now include richer host/path/query suspicion vocabulary:
- Host telemetry/tracking words (`telemetry`, `metrics`, `event`, `ingest`, `track`, `pixel`, etc.).
- Host adtech/tracker vendor examples (`criteo`, `adnxs`, `taboola`, `hotjar`, `mixpanel`, etc.).
- Path telemetry/tracking/exfil words (`/collect`, `/events`, `/identify`, `/exfil`, `/dump`, etc.).
- Query click-id and exfil indicators (`gclid=`, `fbclid=`, `cid=`, `payload=`, `exfil=`, `utm_`).

Vendor names are baseline examples, not comprehensive coverage.

### Hard-block categories
`WebSafetyPolicyOptions.BlockCategories` defaults to:
- `Malware`
- `Phishing`

Evaluation order is:
1. Non-HTTP command: allow
2. Whitelist (`AllowedHosts` / `AllowedDestinations`): allow
3. Explicit block rules: deny
4. Suspicion scoring
5. Any match in `BlockCategories`: deny immediately
6. Threshold deny (`score >= SuspicionThreshold`) when enabled
7. Allow

Whitelist precedence still wins over hard-block categories.
Hard-block deny reasons include category and signal IDs, without leaking query values.

Combinatorial scoring can flag fresh domains by behavior signatures (for example, `data-ingest.io` + `/collect/events` + `cid=` + `payload=`), where `RawScore` exceeds 1.0 and the user-facing score remains clamped to 1.0.

If `score >= SuspicionThreshold` and `BlockSuspiciousByDefault = true`, request is denied.
Whitelist is evaluated before scoring. Explicit block rules are evaluated before scoring.
Suspicion deny reasons include matched signal IDs and destination host/path (not full query values).

## M5.4 hardening note
M5.4 expands the default pure-data signal library and adds category hard-blocking for malware/phishing-style signal matches.

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
This is agent web safety guardrailing, not consumer adblock completeness or a uBlock replacement.


## M6 pointer
M6 adds post-fetch block sanitization via `WebContentSafety` as a second layer after destination policy.
