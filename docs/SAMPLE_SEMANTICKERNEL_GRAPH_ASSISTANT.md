# SAMPLE_SEMANTICKERNEL_GRAPH_ASSISTANT

## Purpose
This sample demonstrates a safe fake Outlook mail/calendar assistant where Dominatus makes decisions and Semantic Kernel is only the capability surface.

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

## Expected behavior
- Mode A: reads mail/calendar, creates draft, does not send/create external effect.
- Mode B: reads mail/calendar, sends approved urgent reply (deterministic first approved action), then can create event.

## Why safer than direct tool access
Dominatus policy and orchestration gates unsafe actions before invocation, so plugin exposure alone cannot send mail/create events without approval.

## Future live adapter notes
A future adapter can replace fake invoker with real Graph-backed functions while preserving allowlist/profile/policy orchestration contracts.

## Non-goals
No OAuth, login, live Graph, real email/calendar effects, planners/agents/MCP/server/UI.
