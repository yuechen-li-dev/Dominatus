# Dominatus.Actuators.Standard M1 — Allowlisted HTTP Actuators

## Purpose

M1 adds safe, typed HTTP text actuators to `Dominatus.Actuators.Standard` using .NET's built-in HTTP stack (`HttpClient`/`HttpMessageHandler`) while preserving strict effect-boundary controls.

This package remains non-LLM and dependency-light.

## Safety model

HTTP access is denied by default unless a host configures named allowlisted endpoints.

Commands cannot provide arbitrary full URLs. They must provide:

- `Endpoint` (name resolved from host options)
- `Path` (relative path under the configured endpoint base URI)

The host controls:

- Available endpoint names
- Base URI and base path containment
- Allowed request headers
- Request/response byte limits
- Timeout
- Redirect behavior

## Public API shape

### Endpoint and options

- `AllowedHttpEndpoint(string Name, Uri BaseUri)`
- `HttpActuatorOptions`
  - `Endpoints`
  - `Timeout` (default: 10s)
  - `MaxResponseBytes` (default: 1,000,000)
  - `MaxRequestBytes` (default: 100,000)
  - `AllowRedirects` (default: `false`)
  - `AllowedRequestHeaders` (default allowlist: `Accept`, `User-Agent`, `Content-Type`)

Validation enforces:

- Endpoint name required
- Absolute `http`/`https` URI only
- No URI user info
- No fragment
- Unique endpoint names (case-insensitive)
- Positive timeout and size limits
- Sensitive header names are rejected in M1

### Commands

- `HttpGetTextCommand`
- `HttpPostJsonCommand`
- `HttpPostTextCommand`

### Result

- `HttpTextResult`
  - `StatusCode`
  - `IsSuccessStatusCode`
  - `Text`
  - `Headers`

Non-success HTTP statuses (e.g. `404`, `500`) return successful actuator completion with `HttpTextResult` and `IsSuccessStatusCode = false`.

Actuator failures are reserved for policy/config/validation/transport/timeout/size-limit failures.

## Request resolution rules

Resolver behavior:

- Unknown endpoint name: reject
- Path must be relative (full absolute URI is rejected)
- Protocol-relative path (`//host/path`) is rejected
- Fragment (`#fragment`) is rejected
- Path escape above endpoint base path (e.g. `../admin`) is rejected
- Query parameters are URL encoded
- Per-command headers must be in allowed header list
- Sensitive headers are rejected (`Authorization`, `Cookie`, `Set-Cookie`, `Proxy-Authorization`, `X-Api-Key`, `X-Auth-Token`)

## Transport behavior

`HttpActuationHandler` uses `HttpClient` with:

- `AllowAutoRedirect = options.AllowRedirects` (default false)
- `UseCookies = false`
- `UseDefaultCredentials = false`
- `Timeout = options.Timeout`

Request and response bodies are byte-capped.

- Request body cap is checked before send.
- Response body cap is enforced while streaming body content.

`HttpPostJsonCommand` validates JSON with `System.Text.Json` before transport.

## Registration helper

```csharp
var httpOptions = new HttpActuatorOptions
{
    Endpoints =
    [
        new AllowedHttpEndpoint("local-api", new Uri("http://localhost:5057/api/"))
    ]
};

var host = new ActuatorHost();
host.RegisterStandardHttpActuators(httpOptions);
```

## Command usage example

```csharp
yield return Ai.Act(
    new HttpGetTextCommand(
        endpoint: "local-api",
        path: "status"),
    LastHttpId);

yield return Ai.Await(LastHttpId, LastHttpResult);
```

`Ai.Act` / `Ai.Await` are authoring helpers from `Dominatus.OptFlow` and are not required by the actuator package.

## Explicit non-goals (M1)

M1 does not add:

- Arbitrary URL/open-internet commands
- Auth/secrets/cookies/OAuth
- Multipart/binary/streaming download
- Retry/polling/scheduling
- Shell/process execution
- Browser automation
- MCP/Telegram/email integrations


## Related docs

- [M1.1 local package smoke verification](ACTUATORS_STANDARD_PACKAGE_SMOKE.md)
