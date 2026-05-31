# Dominatus.SemanticKernelOrchestration

This sample demonstrates the Magentic orchestration pattern as described in Microsoft's Semantic Kernel documentation.

https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/agent-orchestration/magentic?pivots=programming-language-csharp

<img width="2263" height="1493" alt="multi-agent-magentic" src="https://github.com/user-attachments/assets/5e6759b0-9b98-4add-8813-5b34751ce536" />

Everything Microsoft’s Magentic manager LLM has to remember in a prompt, Dominatus represents as explicit runtime state in normal C# code. Orchestration latency is measured in milliseconds, zero live LLM calls are needed for orchestration. 

Microsoft's Magentic manager is an LLM pretending to be a scheduler, Dominatus is a progammable scheduler that can call LLMs to maximize their intelligence.

Semantic Kernel supplies allowlisted plugin functions. Dominatus supplies orchestration, state, policy, trace, and persistence model.

This sample maps Microsoft-style task/progress ledgers and manager loop to Dominatus:
- Task Ledger => `WorldBb`
- Progress Ledger => orchestrator/world blackboard keys
- Next speaker/action => `Ai.Decide`
- Worker actions => mailbox instructions + SK function actuators

No SK planners/agents/orchestration APIs are used.

Persistent mailbox listeners in this sample use sequence/correlation guards stored in blackboards to avoid re-consuming historical events when Ai.Event<T>() waits are reinstalled.
