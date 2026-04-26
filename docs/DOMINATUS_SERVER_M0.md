# Dominatus.Server M0

## Purpose

`Dominatus.Server` is ASP.NET Core server infrastructure for exposing a Dominatus `AiWorld` to custom web UIs through read-only inspection endpoints.

Package name: `Dominatus.Server`.

## What this package is

- Minimal API integration for ASP.NET Core apps.
- A thread-safe runtime wrapper (`DominatusServerRuntime`) around `AiWorld`.
- Read-only JSON endpoints for world/agent inspection.
- DTOs tailored for UI clients.

## What this package is not (M0)

- No bundled dashboard, React/Vite/Blazor client, or static web assets.
- No SignalR stream.
- No write/control endpoints.
- No auth/authz system in M0.
- No LLM, Home Assistant, Stride, or Godot coupling.

## Usage with ASP.NET Core Minimal API

```csharp
var builder = WebApplication.CreateBuilder(args);

var world = new AiWorld();
var runtime = new DominatusServerRuntime(world);

builder.Services.AddDominatusServer(runtime);

var app = builder.Build();

app.MapDominatusServer();

_ = Task.Run(async () =>
{
    while (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
    {
        runtime.Write(world => world.Tick(1f / 30f));
        await Task.Delay(TimeSpan.FromMilliseconds(33));
    }
});

app.Run();
```

You can also call `AddDominatusServer(world)` to wrap directly.

## Thread-safety model

`AiWorld` is conceptually single-threaded. HTTP requests may run concurrently with host ticking. Use `DominatusServerRuntime` gate methods:

- `Read(Func<AiWorld,T>)` for endpoint reads.
- `Write(Action<AiWorld>)` for world ticks and other host-side writes.

`DominatusServerRuntime.World` is available as an escape hatch, but direct concurrent access bypasses synchronization.

## Endpoints (read-only)

Default prefix: `/dominatus`.

- `GET /dominatus/health`
- `GET /dominatus/world`
- `GET /dominatus/world/blackboard`
- `GET /dominatus/agents`
- `GET /dominatus/agents/{id}`
- `GET /dominatus/agents/{id}/blackboard`
- `GET /dominatus/agents/{id}/path`
- `GET /dominatus/agents/{id}/snapshot`
- `GET /dominatus/snapshots`

Unknown agent ids return `404`.

## DTOs

- `DominatusHealthDto`
- `DominatusWorldDto`
- `DominatusAgentDto`
- `DominatusBlackboardDto`
- `DominatusBlackboardEntryDto`
- `DominatusAgentPathDto`
- `DominatusAgentSnapshotDto`

Blackboard values are rendered as typed strings (`bool`, `int`, `long`, `float`, `double`, `string`, `guid`, `unknown`, `null`) and include TTL expiry (`ExpiresAt`) when present.

## Building a web UI on top

A web client can poll the inspection endpoints to render:

- session/world summary (`/world`),
- agent roster (`/agents`),
- selected-agent details (`/agents/{id}`, `/path`, `/snapshot`),
- blackboards (`/world/blackboard`, `/agents/{id}/blackboard`).

M0 intentionally does not include a client implementation so apps can choose their own UI stack.

## Security note

M0 does not provide authentication/authorization. Production hosts should add auth/authz policies around these endpoints.

## Future work

- Live state streaming (e.g., SignalR).
- Controlled write endpoints.
- Built-in auth/authz integration guidance.
