# ACTUATORS_STANDARD_M6_WEB_CONTENT_SAFETY

## Purpose
M6 adds a post-fetch content-safety scaffold for caller-provided web content blocks. It scores blocks and sanitizes output before LLM context/reasoning.

## How it differs from HTTP WebSafety
- **HTTP WebSafety (M5)**: pre-fetch destination safety (URL/host/path policy before transport).
- **WebContentSafety (M6)**: post-fetch block-level sanitization of fetched content.

Both are policy/scoring helpers, not complete browser security products.

## Threat model
Even same-origin content can contain sponsored widgets, prompt-injection text, unsafe download CTAs, and tracking/affiliate links.

## Block model
`WebContentBlock` models extracted blocks (`Text`, `Link`, `Image`, `Script`, `IFrame`, `Download`, `Unknown`) with optional text/url/label/class/source hints.

## Signal model
`WebContentSafetySignal` uses deterministic target+pattern matching (`TextContains`, `LabelContains`, `ClassOrIdContains`, `UrlContains`, `SourceHintContains`, `KindIs`) with weighted categories.

## Default signals
Baseline signals cover Advertisement, Sponsored, Tracker, PromptInjection, UnsafeDownload, Affiliate, and Suspicious patterns.

## SafeText output
`WebContentSafetyReport.SafeText` includes only kept blocks and joins rendered block text with configured separator.

## LLM handoff
Use the sequence:
1. HTTP destination policy (M5)
2. Fetch
3. WebContentSafety evaluation
4. Send `SafeText` + provenance to `Llm.Context`
5. Use sanitized packets for `Llm.Call` / `Llm.Decide`

## Limitations and non-goals
No browser engine, HTML/DOM parser, JS execution, OCR, ML/LLM classification, network calls, external lists, Core/MCP/SemanticKernel dependencies, or endpoint changes.
