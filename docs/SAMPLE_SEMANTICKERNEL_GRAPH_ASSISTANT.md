# SAMPLE_SEMANTICKERNEL_GRAPH_ASSISTANT

## Purpose
This sample demonstrates a safe fake Outlook mail/calendar assistant where Dominatus makes decisions, `Llm.Call` drafts human-facing text, and Semantic Kernel is only the capability surface.

## Architecture
Microsoft Graph/Outlook capabilities are modeled as fake Semantic Kernel functions:
- graph.mail.list_messages
- graph.mail.read_message
- graph.mail.create_draft
- graph.mail.send_message
- graph.calendar.list_events
- graph.calendar.create_event

No live Graph SDK or network calls are used.

## Safety model
- Capability profile: `SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendar()`.
- Allowlist mode A (no approval): read + `graph.mail.create_draft`.
- Allowlist mode B (approval): read + draft + send + create event.
- `ActuationPolicy` denies `send_message` and `create_event` unless approval is granted.

## Ai.Decide usage
The sample uses `Ai.Decide` on slot `GraphAssistant.NextAction` with deterministic options:
- DraftReply
- SendApprovedReply
- CreateCalendarEvent
- Idle

Default weighting keeps urgent reply handling first:
- no approval => `DraftReply`
- approval granted => `SendApprovedReply`

## M1 LLM draft generation (fake/no-live)
M1 adds `Dominatus.Llm.OptFlow` and executes a real `Llm.Call` step with:
- stableId: `graph-assistant.draft-urgent-reply`
- intent: draft concise urgent deployment-status reply
- persona: concise professional Outlook assistant
- context: urgent subject/sender/body + calendar summary + approval mode
- outputs stored in blackboard keys:
  - `GraphAssistant.DraftText`
  - `GraphAssistant.DraftJson`

The sample uses `FakeLlmClient` + in-memory cassette mode, so no provider/network/model calls occur.

## Expected behavior
- Mode A (`Run(false)`): reads mail/calendar, uses `Llm.Call` to generate draft text, creates draft with generated text, does not send mail, does not create event.
- Mode B (`Run(true)`): reads mail/calendar, uses `Llm.Call`, sends generated urgent reply text through approved send action.

## Why this demonstrates the safe assistant stack
The end-to-end flow is explicit and testable:
Graph profile ➜ allowlist ➜ `Ai.Decide` ➜ `Llm.Call` draft text ➜ approval gate ➜ SK function execution.

This demonstrates automation safety for Outlook-like tasks without exposing unsafe send/create behavior by default.

## Future live adapter notes
A future adapter can replace fake invokers with real Graph and real LLM handlers while preserving allowlist/profile/policy/orchestration contracts.

## Non-goals
No OAuth, login, live Graph, real email/calendar effects, live LLM providers, planners/agents/MCP/server/UI.
