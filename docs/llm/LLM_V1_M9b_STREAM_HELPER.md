# LLM V1 M9b: Stream authoring helper

## Purpose
M9b adds `Llm.Stream(...)` as the authored-node streaming sibling of `Llm.Call(...)`.

## API
- Builder overload: `Llm.Stream(stableId, intent, persona, streamId, context, storeTextAs, ...)`
- Packet overload: `Llm.Stream(stableId, intent, persona, streamId, packet, storeTextAs, ...)` delegates to `context: c => c.AddPacket(packet)`.

## Stored outputs
- `storeTextAs`: final accumulated stream text.
- `storeSnapshotJsonAs` (optional): deterministic final snapshot JSON.
- `storeStreamIdAs` (optional): final stream id.
- `storeStatusAs` (optional): final status text (`Completed`, `Failed`, `Cancelled`).

## Snapshot JSON
Serialized from final `LlmStreamSnapshot` and includes packet provenance metadata in `contextPacket` when packet overload is used.

## Runtime semantics
- Dispatches `LlmStreamCommand`.
- Waits for `ActuationCompleted<LlmStreamSnapshot>`.
- Stores outputs.
- Re-entry restores outputs from internal cache and does not redispatch.

## Failure/cancellation semantics
`Failed` and `Cancelled` snapshots still complete the authored step and persist partial text + status.
Only missing/failed actuation completion without payload throws.

## Chunk events
Chunk events remain published by the M9a actuation handler as `LlmStreamChunkAvailable` on `ctx.Agent.Events`.

## Non-goals
No live provider streams, no reconnect endpoints, no UI, no tool-calling, no SK/MCP, no core persistence format changes.

## Future (M9c)
Server reconnect/transport endpoints remain future work.


## Server integration

M9b stream outputs can be consumed by `DominatusLlmStreamRegistry`; see `docs/server/DOMINATUS_SERVER_M1_STREAMS.md`.


- Durable server read + SSE consumption path is documented in `docs/server/DOMINATUS_SERVER_M2_STREAM_SSE.md`.
