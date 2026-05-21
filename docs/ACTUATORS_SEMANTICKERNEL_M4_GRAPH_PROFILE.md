# Dominatus.Actuators.SemanticKernel M4 — Microsoft Graph Capability Profile

## Purpose
M4 adds a Microsoft Graph / Outlook mail-calendar **capability profile** for Semantic Kernel actuation in Dominatus.

This is **Graph-through-SK**, not a native Graph actuator:

1. Host exposes Graph/Outlook operations as SK plugin functions.
2. Dominatus classifies those functions with `SemanticKernelCapabilityProfile`.
3. Profile entries are converted to allowlists (`AllowedSemanticKernelFunction`).
4. Actuation policy/workflow gates risky functions.
5. `SemanticKernelFunctionCommand` invokes only allowlisted functions.

## Explicit non-goals in M4
- No `Microsoft.Graph`, `Azure.Identity`, MSAL, OAuth, device login, or Graph SDK adapter.
- No live Graph calls and no network-based tests.
- No native Graph command/handler types.

## Profile factory API
Use `SemanticKernelMicrosoftGraphProfiles`:

- `OutlookMailCalendar(string pluginPrefix = "graph")`
- `OutlookMailCalendarReadAllowlist(string pluginPrefix = "graph")`
- `OutlookMailCalendarWriteAllowlist(string pluginPrefix = "graph")`
- `OutlookMailCalendarExternalEffectAllowlist(string pluginPrefix = "graph")`

`pluginPrefix` is required, cannot contain whitespace, and trailing `.` is trimmed.

## Outlook Mail/Calendar profile
Profile id/title:
- `microsoft-graph.outlook-mail-calendar`
- `Microsoft Graph Outlook Mail/Calendar`

Entries:
- `graph.mail.list_messages` — Read
- `graph.mail.read_message` — Read
- `graph.mail.create_draft` — Write (approval default: false)
- `graph.mail.send_message` — ExternalEffect (approval: true)
- `graph.calendar.list_events` — Read
- `graph.calendar.create_event` — Write (approval: true)
- `graph.calendar.update_event` — Write (approval: true)
- `graph.calendar.cancel_event` — Destructive (approval: true)

## Approval guidance
`RequiresHumanApproval` in profile entries is **guidance metadata**, not runtime enforcement by itself.

Runtime enforcement should be done by `ActuationPolicy` and orchestration workflow checks.

## Examples
```csharp
var profile = SemanticKernelMicrosoftGraphProfiles.OutlookMailCalendar();

var readAllowlist = profile.ToAllowedFunctions(
    SemanticKernelCapabilityProfilePredicates.IsReadOnly);

var options = new SemanticKernelActuatorOptions
{
    AllowedFunctions = readAllowlist
};
```

```csharp
var effectAllowlist = profile.ToAllowedFunctions(e =>
    e.Risk == SemanticKernelCapabilityRisk.ExternalEffect);

// Do not invoke send_message until human approval and rationale are present.
```

## Future work
- Typed façade that emits `SemanticKernelFunctionCommand`.
- Live SK Graph plugin sample wiring.
- Additional profiles (Teams/OneDrive/Planner/ToDo).
- Stronger approval workflow sample with persisted rationale and audit chain.
