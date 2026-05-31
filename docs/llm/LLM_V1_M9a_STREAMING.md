# LLM V1 M9a — Durable Streaming Foundation

## Purpose
M9a introduces the first durable streaming foundation in `Dominatus.Llm.OptFlow` using fake-provider streams only.

## Boundary model
- Provider boundary: `ILlmStreamingClient.StreamAsync(...)` returns `IAsyncEnumerable<LlmStreamDelta>`.
- Dominatus runtime: records durable indexed chunks and accumulated stream snapshot state.
- Dominatus node authoring remains deterministic `IEnumerator<AiStep>`.

## Why `IAsyncEnumerable` only at provider edge
Provider streams are ephemeral transport mechanics. Dominatus state must remain durable and replayable as indexed chunks plus snapshot status.

## Core types
- `LlmStreamStatus`: `Pending`, `Streaming`, `Completed`, `Failed`, `Cancelled`.
- `LlmStreamDelta`: provider delta text + optional finish reason + final flag.
- `LlmStreamChunk`: durable indexed chunk for a stream id.
- `LlmStreamSnapshot`: durable stream state (request hash, status, next index, accumulated text, finish reason, error).
- `LlmStreamChunkAvailable`: event payload when chunk is recorded.

## M9a scope
- Fake provider streaming client and tests.
- Stream recorder behavior and tests.
- Actuation handler integration and event publication tests.
- No live provider streaming, no server/UI reconnect endpoints.

## Status semantics
- Completed: async stream ended normally.
- Failed: exception occurred; partial output preserved.
- Cancelled: cancellation observed; partial output preserved.

## Future milestones
- M9b: `Llm.Stream(...)` authoring helper.
- M9c: server reconnect endpoints.
- M9d: live provider streaming implementations.


- See M9b helper: `docs/llm/LLM_V1_M9b_STREAM_HELPER.md`.


## Server integration

Durable stream inspection/reconnect over HTTP is documented in `docs/server/DOMINATUS_SERVER_M1_STREAMS.md`.


- Server integration note: durable stream SSE endpoint is documented in `docs/server/DOMINATUS_SERVER_M2_STREAM_SSE.md`.
