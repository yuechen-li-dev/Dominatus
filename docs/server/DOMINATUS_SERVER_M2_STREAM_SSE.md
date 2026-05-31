# Dominatus.Server M2: Durable stream SSE endpoint

## Purpose

M2 adds a read-only Server-Sent Events (SSE) endpoint that streams **durable** LLM chunks from `DominatusLlmStreamRegistry`.

Provider streaming remains ephemeral. Dominatus registry state remains the source of truth.

## Endpoint

- `GET /dominatus/streams/{streamId}/events`
- `GET /dominatus/streams/{streamId}/events?after=N`

Behavior:

- `after` defaults to `-1`
- `after < -1` returns `400`
- unknown `streamId` returns `404`
- emits chunks where `index > after`
- then waits for new chunks recorded for that stream
- completes when terminal state is reached (`Completed`, `Failed`, `Cancelled`) via final chunk or terminal snapshot

## Event format

Chunk event:

```text
event: chunk
id: {chunk.Index}
data: {json LlmStreamChunkDto}
```

Status event (terminal):

```text
event: status
data: {"streamId":"s1","status":"Completed","nextChunkIndex":3,"finishReason":"stop","error":null}
```

## Reconnect semantics

Clients reconnect using last seen chunk index:

- if last seen index is `7`, reconnect with `?after=7`
- endpoint emits only `index > 7`

This mirrors M1 `/chunks?after=N` semantics and adds a live tail.

## Relation to M1 read endpoints

M1 REST reads remain unchanged:

- `/dominatus/streams`
- `/dominatus/streams/{streamId}`
- `/dominatus/streams/{streamId}/chunks`

M2 adds a live SSE view over the same registry state.

## Non-goals

No SignalR, WebSockets, provider-stream proxying, write/cancel endpoints, auth/UI, or Core/Llm behavior changes.
