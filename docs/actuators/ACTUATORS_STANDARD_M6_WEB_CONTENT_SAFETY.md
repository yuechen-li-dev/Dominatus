# ACTUATORS_STANDARD_M6_WEB_CONTENT_SAFETY

## Purpose
M6 adds a deterministic post-fetch content-safety layer for caller-provided web content blocks. M6.1 hardens the LLM handoff so omitted content is visible instead of silently erased.

## Honest SafeText handoff
`WebContentSafetyReport` now exposes omission diagnostics:
- `HasOmissions`
- `OmissionSummary`
- `OmittedBlockRecords`

This keeps downstream models/tools aware of what was removed and why.

## Omission annotations
By default, `SafeText` includes inline sentinels in original block order:
`[CONTENT OMITTED: <Category>; block=<id>; signals=<top-signal-ids>]`

`AnnotateOmissions=false` restores kept-only rendering.

## OmittedBlockRecords
Each omitted block provides:
- `BlockId`
- `Category`
- `RawScore` / `Score`
- `TopSignalIds` (deterministic, bounded by `MaxOmissionSignalIds`)

## Markdown-style link rendering
Kept block rendering preserves link semantics:
- Link: `[Label](Url)` / `[link](Url)`
- Download: `[download: Label](Url)` / `[download](Url)`
- Image: `[image: Label]` or `[image](Url)`

## Prompt-injection hardening
Default signal coverage now includes stronger direct override/exfiltration/script patterns and dangerous URL schemes (`javascript:`, `data:`).

## False-positive tuning
Overbroad prompt-injection phrases like `follow these instructions` and `system prompt` are downgraded to weak `Suspicious` signals so legitimate documentation/instructions are not hard-omitted by themselves.

## Dangerous links and shorteners
Shortener signals (e.g., `bit.ly`) are added as `Suspicious`; high-risk schemes remain prompt-injection/hard-omit paths.

## Limitations
This remains a scoring helper, not a browser/parser/adblocker. No DOM/CSS parsing, JS execution, OCR, ML/LLM classification, external blocklists, live network policy calls, or Core/MCP/endpoint changes.
