# Dominatus.Actuators.SemanticKernel M3 — Capability Profiles

## Purpose

M3 adds a reusable capability-profile layer to classify Semantic Kernel functions by risk and convert reviewed profile entries into explicit Dominatus allowlists.

Capability profiles are **host-facing classification**, not runtime permission.

## Core doctrine

- Semantic Kernel exposes capabilities.
- Dominatus profiles risk.
- Dominatus allowlists exact functions.
- Dominatus workflow decides approval before writes/effects/destructive actions.

Runtime permission remains `SemanticKernelActuatorOptions.AllowedFunctions`.

Discovery can inform humans, but discovery must not auto-grant capability.

## Risk tiers

`SemanticKernelCapabilityRisk`:

- `Read`: observes data without changing external state.
- `Write`: creates/modifies external state.
- `ExternalEffect`: sends/posts/triggers/notifies/charges/publishes/starts jobs.
- `Destructive`: deletes/revokes/cancels/removes/irreversible high-risk change.
- `Unknown`: unclassified; should not be casually allowlisted in production.

## API and conversion

Use `SemanticKernelCapabilityProfileEntry` to classify plugin/function pairs, then build allowlists with `SemanticKernelCapabilityProfile.ToAllowedFunctions(...)`.

```csharp
var graphProfile = new SemanticKernelCapabilityProfile(
    Id: "graph.personal-assistant",
    Title: "Graph personal assistant profile",
    Entries:
    [
        new("graph.mail", "list_headers", SemanticKernelCapabilityRisk.Read),
        new("graph.mail", "send_message", SemanticKernelCapabilityRisk.ExternalEffect, RequiresHumanApproval: true),
        new("graph.calendar", "create_event", SemanticKernelCapabilityRisk.Write, RequiresHumanApproval: true)
    ]);

var readOnlyAllowlist = graphProfile.ToAllowedFunctions(e => e.Risk == SemanticKernelCapabilityRisk.Read);

var options = new SemanticKernelActuatorOptions
{
    AllowedFunctions = readOnlyAllowlist
};
```

## Approval boundary

Write/effect/destructive routing is workflow policy, not actuator policy.

Conceptual write flow:

1. Dominatus workflow decides a message/event/task should be created.
2. Human approval gate records rationale and `ApprovedBy`.
3. Workflow invokes write/effectful SK function via allowlisted command.

## Examples by ecosystem style

- Graph-style: `graph.mail.list_headers` (Read), `graph.mail.send_message` (ExternalEffect), `graph.files.delete_file` (Destructive).
- OpenAPI-style: `crm.contacts.get` (Read), `crm.contacts.upsert` (Write), `billing.invoices.charge` (ExternalEffect).
- Vector/RAG-style: `vector.index.query` (Read), `vector.index.upsert` (Write), `vector.index.delete_namespace` (Destructive).

## Relation to MCP-through-SK (M2)

M2 doctrine is unchanged:

- no native MCP dependency in this actuator package
- no auto-allow from metadata discovery
- profile classification is additive guidance for safer allowlist construction

## Non-goals

M3 does not add:

- Microsoft Graph runtime dependency
- Graph calls
- OpenAPI import runtime
- vector connectors
- Bing/web connectors
- MCP or A2A runtime dependencies
- live LLM/provider credential paths
- server endpoint changes
- approval runtime logic inside SK actuator
- automatic allowlisting from discovery
