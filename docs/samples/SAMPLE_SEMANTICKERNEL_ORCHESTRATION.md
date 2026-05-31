# Semantic Kernel orchestration sample (M0)

Purpose: show Microsoft-style task/progress ledger orchestration using Dominatus runtime primitives.

- Task Ledger = `WorldBb`
- Progress Ledger = orchestrator bb + world bb
- Next speaker/action = `Ai.Decide`
- Worker instructions/reports = mailbox messages
- SK functions = allowlisted semantic kernel actuator commands
- Metadata catalog = inspection only

No live model calls, API keys, planners, agents, orchestration APIs, or network.

Run:

```bash
dotnet run --project samples/Dominatus.SemanticKernelOrchestration/Dominatus.SemanticKernelOrchestration.csproj
```
