# LLM Context M4: Packet Manifest and Omission Diagnostics

M4 adds auditable packet diagnostics to show included chunks, omitted chunks, and omission reasons (expired, kind filter, include tag filter, exclude tag filter, budget exceeded).

It introduces `LlmContextPacket.Diagnostics`, `MaxChars`, `RemainingChars`, `WasBudgetConstrained`, plus `LlmContextPacketManifest` and `LlmContextPacketManifestJson`.

Dogfood now emits per-packet `.manifest.json` files and asks reviewers to inspect markdown packets together with manifests.

Non-goals remain unchanged: no provider integrations, no live LLM calls, no SK/MCP/OptFlow integration.
