# LLM Context M2: `.context` Binary Container

M2 adds a durable binary save-file container beside the existing M0 JSON codec and M1 loadouts.

## Format
- Magic: `DCTX` (4 ASCII bytes)
- Endian: little-endian for fixed header integers
- Header (32 bytes):
  - formatVersion (int32, current 1)
  - headerSize (int32, fixed 32)
  - chunkCount (int32)
  - directoryOffset (int64)
  - directoryLength (int64)
- Layout:
  - `[fixed header][directory JSON UTF-8][raw payload chunks]`

## Directory
Directory is JSON and includes chunk entries:
- `id`, `kind`, `format`, `version`, `offset`, `length`, `createdUtc`, `updatedUtc`.

## Required chunk
Every file currently includes a required store chunk:
- `id`: `context.store`
- `kind`: `context-store`
- `format`: `application/vnd.dominatus.llm.context.store+json`
- `version`: `1`
- payload: M0/M1 `LlmContextStoreJson.Serialize(store)` UTF-8.

## API
Use `LlmContextContainer`:
- `WriteToBytes` / `ReadStore`
- `Save` / `Load`
- `ReadManifest`
- Stream overloads: `Write(Stream, ...)`, `Read(Stream)`, `ReadManifest(Stream)`.

## Validation
Container read rejects invalid magic/version/header, malformed directory json, duplicate IDs, missing store chunk, out-of-bounds chunk ranges, unsupported store chunk format/version, and malformed store payload.

## Why no compression/encryption/checksum in M2
M2 focuses on deterministic structure and compatibility. Compression/encryption/checksums are deferred for future format revisions.
