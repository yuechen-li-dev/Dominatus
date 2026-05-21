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

No live Graph SDK, identity SDK, or network calls are used.

## Safety model
- Capability profile: `SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendar()`.
- Allowlist mode A (no approval): read + `graph.mail.create_draft`.
- Allowlist mode B (approval): read + draft + send + create event.
- `ActuationPolicy` denies `send_message` and `create_event` unless approval is granted.

## Scenario API (M2)
`GraphAssistantDemo.Run` now supports deterministic scenarios:
- `Run(bool approvalGranted)` keeps M1 urgent-reply compatibility.
- `Run(bool approvalGranted, GraphAssistantScenario scenario, TextWriter? output = null)` enables:
  - `GraphAssistantScenario.UrgentReply`
  - `GraphAssistantScenario.SchedulingRequest`

## Ai.Decide usage
The sample uses `Ai.Decide` on slot `GraphAssistant.NextAction` with deterministic options:
- DraftReply
- SendApprovedReply
- DraftMeetingProposal
- CreateApprovedCalendarEvent
- Idle

Selection is scenario-aware:
- UrgentReply + no approval => `DraftReply`
- UrgentReply + approval => `SendApprovedReply`
- SchedulingRequest + no approval => `DraftMeetingProposal`
- SchedulingRequest + approval + free slot => `CreateApprovedCalendarEvent`

## LLM draft generation (fake/no-live)
The sample executes real `Llm.Call` steps with fake local infrastructure (`FakeLlmClient` + in-memory cassette):
- `graph-assistant.draft-urgent-reply`
- `graph-assistant.draft-meeting-proposal`

Meeting proposal call details:
- intent: draft concise proposal from scheduling email + available slot
- persona: concise professional Outlook scheduling assistant
- context: scheduling subject/sender/body, slot, timezone (UTC)
- output keys:
  - `GraphAssistant.MeetingProposalText`
  - `GraphAssistant.MeetingProposalJson`

The fake deterministic meeting proposal text includes the stable phrase:
- `I can meet next Tuesday afternoon...`

## Expected behavior
- `UrgentReply` no approval: generate urgent reply text, create mail draft only.
- `UrgentReply` approval: generate urgent reply text, send approved mail.
- `SchedulingRequest` no approval: generate meeting proposal text, optionally draft mail response, do not create calendar event.
- `SchedulingRequest` approval: generate meeting proposal text, create fake calendar event (`event-created:meeting-next-week`), no mail send.

## Why this demonstrates the safe assistant stack
Graph profile ➜ allowlist ➜ `Ai.Decide` ➜ `Llm.Call` draft/proposal text ➜ approval gate ➜ fake SK function execution.

This demonstrates both Outlook mail and calendar workflows without live Graph or live model risk.

## Non-goals
No OAuth, login, live Graph, real email/calendar effects, live LLM providers, planners/agents/MCP/server/UI.
