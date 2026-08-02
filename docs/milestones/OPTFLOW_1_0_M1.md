# OPTFLOW-1.0-M1 — Inspectable Flow Definitions

M1 adds immutable `FlowState` and `FlowDefinition` authoring data to `Dominatus.OptFlow`. Definitions snapshot supported HFSM options, validate explicit metadata, inspect deterministically, and build fresh Core graphs. No `Dominatus.Core` runtime or persistence schema changed.

Dogfood migrations: Ariadne Thread of Night, Rust Simulator, the Ariadne console catalog/runtime, and the Home Assistant thermostat utility fixture. Their IDs, iterator bodies, and HFSM semantics remain explicit.

Future milestones may consider durable operation sites, retry/result-routing vocabulary, richer explicit transition metadata, and package version/release policy. Those are deliberately deferred from M1.
