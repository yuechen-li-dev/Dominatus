# Dominatus Server M1: Durable LLM Stream Read/Reconnect Endpoints

M1 adds read-only HTTP endpoints for durable LLM stream state.

## Purpose

Provider streaming is ephemeral; Dominatus stream state is durable and inspectable. Hosts can record chunk events and final snapshots, and clients can reconnect by chunk index.

## Registry API

`DominatusLlmStreamRegistry` in `Dominatus.Server`:

- `RecordChunk(LlmStreamChunkAvailable chunk)`
- `RecordSnapshot(LlmStreamSnapshot snapshot)`
- `ListStreams()`
- `GetStream(streamId)`
- `GetChunks(streamId, after = -1)`

Behavior:

- thread-safe (`lock`)
- duplicate same index + same payload is idempotent
- duplicate same index + different payload throws
- stream detail can still return `TextSoFar` from snapshot even without chunk history

## Host responsibilities

- Observe `LlmStreamChunkAvailable` and call `RecordChunk`.
- Record final `LlmStreamSnapshot` when available and call `RecordSnapshot`.

## Endpoints

- `GET /dominatus/streams`
- `GET /dominatus/streams/{streamId}`
- `GET /dominatus/streams/{streamId}/chunks`
- `GET /dominatus/streams/{streamId}/chunks?after=N`

Reconnect semantics: if client has seen chunk index `N`, request `after=N` to fetch missing chunks (`Index > N`).

Validation:

- unknown stream => `404`
- invalid `after < -1` => `400`

## Scope and non-goals

M1 is read-only HTTP inspection only.

Not included: SSE, SignalR, WebSockets, provider stream passthrough, write/cancel endpoints, auth, frontend.


## M2 update

SSE live stream endpoint added in M2: `/dominatus/streams/{streamId}/events` with `?after=N` reconnect semantics over the same durable registry. See `docs/server/DOMINATUS_SERVER_M2_STREAM_SSE.md`.
