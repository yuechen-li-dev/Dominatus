# Parallel Module Workflow sample

`samples/Dominatus.ParallelModuleWorkflow` is the intentionally boring parallel-agentic-work demo for Dominatus M0 parallel module orchestration.

It demonstrates the thesis that LangGraph/CrewAI-style systems often parallelize prompt chains, while Dominatus can parallelize ordinary deterministic workflows whose semantic leaf work may call LLMs. LLMs may author or review the workflow, but runtime execution is just inspectable C#.

## Purpose

The sample models a fake multi-module implementation workflow:

1. `Auth` designs the shared contract first.
2. `Api`, `Database`, and `Frontend` depend on that contract.
3. The coordinator launches those three independent module workers with host-level `Task.WhenAll`.
4. The coordinator merges completed module results in deterministic order: `Auth`, `Api`, `Database`, `Frontend`.

The sample has no live LLM provider, no network, no server endpoint, no UI, and no shared-world parallel ticking.

## Why this is not “run 5 LLM agents on the same task”

This demo is not a group-chat or prompt-chain race. The coordinator is deterministic and dependency-aware. It first obtains a contract from `Auth`, then runs only the modules that are independent after that contract exists.

Each module owns its own tiny Dominatus runner/world/agent path and performs one semantic leaf action through `Llm.Call`. The fake LLM output is deterministic by stable ID:

- `parallel.auth.design-contract`
- `parallel.api.implement`
- `parallel.database.implement`
- `parallel.frontend.implement`

The model call is the leaf operation, not the scheduler.

## Dependency-aware parallelism

The workflow has three phases:

### Phase 1: Auth contract

`Auth` runs first and returns:

```text
AuthContract v1: endpoints=/login,/refresh; token=jwt; userId=string
```

### Phase 2: independent dependent modules

Once the contract is ready, the coordinator starts `Api`, `Database`, and `Frontend` with `Task.WhenAll` over independent worker tasks. Each worker receives the shared Auth contract in the `Llm.Call` context and writes only its local result.

### Phase 3: deterministic merge

After `Task.WhenAll` completes, the coordinator merges results in fixed module order and emits a final report containing the contract, module statuses, dependency evidence, and parallelism evidence.

## No shared mid-tick mutation

M0 deliberately avoids same-world parallel tick scheduling. There is no shared `AiWorld` mutation while the parallel module workers run. Each worker creates its own tiny Dominatus world/agent/context and returns a `ModuleResult` to the coordinator.

That means this sample does **not** require Core thread-safety changes, `WorldBb` concurrent write semantics, `world.FlushPendingWorldBbWrites`, or a scheduler redesign.

## Fake/no-live Llm.Call

The sample uses a sample-local deterministic fake `ILlmClient` registered behind `LlmTextActuationHandler`. The module workflow executes the real `Llm.Call` helper, which dispatches through the Dominatus actuation path. The fake client returns scripted text by stable ID and never performs network I/O.

## Parallelism proof strategy

The fake LLM client records in-flight calls. For the dependent modules only, it waits until all three stable IDs have reached the fake provider before releasing any of them. This deterministic async barrier proves the coordinator launched `Api`, `Database`, and `Frontend` concurrently instead of running them serially.

Tests assert `MaxObservedConcurrency >= 3`, dependency ordering, and deterministic merge order.

## Contrast with prompt-chain parallelism

Prompt-chain frameworks commonly parallelize prompts directly: fan out model calls, collect text, and ask another prompt to synthesize. This sample instead parallelizes normal C# workflow units. Dominatus owns the dependency ordering, worker isolation, actuation boundary, fakeability, and merge semantics; the LLM call remains a replaceable semantic leaf.

## Future work

This M0 sample intentionally stays safe and boring. Future work could explore:

- true same-world parallel tick scheduling;
- staged `WorldBb` writes and explicit merge semantics;
- provider rate-limit-aware LLM batches;
- real provider use through `RankedLlmClient` and OpenRouter after policy, cassette, and rate-limit boundaries are selected.

## Run

```bash
dotnet run --project samples/Dominatus.ParallelModuleWorkflow/Dominatus.ParallelModuleWorkflow.csproj --framework net10.0
```

## Test

```bash
dotnet test tests/Dominatus.ParallelModuleWorkflow.Tests/Dominatus.ParallelModuleWorkflow.Tests.csproj --framework net10.0
```
