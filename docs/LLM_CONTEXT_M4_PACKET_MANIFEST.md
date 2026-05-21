# LLM Context M4: Packet Manifest and Omission Diagnostics

M4 adds auditable packet diagnostics to show included chunks, omitted chunks, and omission reasons (expired, kind filter, include tag filter, exclude tag filter, budget exceeded).

It introduces `LlmContextPacket.Diagnostics`, `MaxChars`, `RemainingChars`, `WasBudgetConstrained`, plus `LlmContextPacketManifest` and `LlmContextPacketManifestJson`.

Dogfood now emits per-packet `.manifest.json` files and asks reviewers to inspect markdown packets together with manifests.

### Structured packet provenance

M4.1 adds `LlmContextPacketProvenance` and `LlmContextPacketSourceKind` to make packet origin deterministic and machine-readable.

- `SourceKind` is `Query` or `Loadout`.
- Loadout packets include `LoadoutId`, `LoadoutTitle`, and `LoadoutDescription`.
- Query and loadout packets both carry selection fields in provenance (`IncludeKinds`, `RequiredChunkIds`, `IncludeTags`, `ExcludeTags`, `MaxChars`, `IncludeExpired`).
- `LlmContextPacket` and `LlmContextPacketManifest` now include `Provenance`.
- `QuerySummary` remains for human-readable context, but tooling should use `Provenance` rather than parsing `QuerySummary`.

Non-goals remain unchanged: no provider integrations, no live LLM calls, no SK/MCP/OptFlow integration.
