# Dominatus.SemanticKernelOrchestration

Semantic Kernel supplies allowlisted plugin functions. Dominatus supplies orchestration, state, policy, trace, and persistence model.

This sample maps Microsoft-style task/progress ledgers and manager loop to Dominatus:
- Task Ledger => `WorldBb`
- Progress Ledger => orchestrator/world blackboard keys
- Next speaker/action => `Ai.Decide`
- Worker actions => mailbox instructions + SK function actuators

No SK planners/agents/orchestration APIs are used.

Persistent mailbox listeners in this sample use sequence/correlation guards stored in blackboards to avoid re-consuming historical events when Ai.Event<T>() waits are reinstalled.
