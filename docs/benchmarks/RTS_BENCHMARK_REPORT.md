# Dominatus.RTSBenchmark Report

_Run date: 2026-06-02 for the M11 Core parallel refresh. Baseline sensor/cadence, checkpoint/resume, Battle, and NativeAOT notes are retained from the 2026-06-01 report where explicitly described. Benchmark configuration: Release `dotnet run` on `net10.0`, except for the explicitly marked NativeAOT smoke run._

## Executive summary

- **Dominatus.RTSBenchmark is a pure behavioral-AI CPU benchmark.** It measures stateful RTS-like ships making local utility decisions, exchanging events, and emitting deterministic action intents.
- **The measured loop excludes GPU/rendering/windowing, network, disk I/O, live model inference, and provider calls.** JSON/CSV writing happens after the measured simulation loop.
- **The benchmark uses actual Dominatus primitives:** one `AiAgent` per ship, HFSM execution, `Ai.Decide`, per-agent blackboards, mailbox/event coordination, tactical summaries, and a deterministic action buffer/resolution phase.
- **Skirmish sensor/cadence baseline from the prior report on this machine:** `SpatialGrid + cadence` reached **130,862.42 median agent-ticks/sec** and **1,177,761.79 median utility option evaluations/sec** over 5 sequential trials.
- **SpatialGrid + dynamic cadence improves throughput at Skirmish scale.** The best Skirmish median was `SpatialGrid + cadence`, **36.5% above BroadScan no cadence** and **46.5% above SpatialGrid no cadence** on this run set.
- **Deterministic hashes were stable across repeated trials.** Each Skirmish sensor/cadence configuration produced one stable hash across all 5 trials.
- **The generic Core `ParallelAiWorldRunner` now runs the RTSBenchmark safe subset and preserves deterministic hash equivalence with sequential execution.** This is a major proof point for Dominatus as a deterministic agent runtime/control kernel because the benchmark is no longer limited to a benchmark-local parallel path.
- **Fresh M11 Skirmish agent-parallelism comparison results on this machine:** Sequential, `LocalParallelDecision`, and `CoreParallelRunner` all produced hash `535c9b8e5f5d01e1`. Median agent-ticks/sec were **76,942.79** sequential, **74,149.66** benchmark-local parallel (**0.96x**), and **68,625.18** Core staged parallel (**0.89x**) over 5 trials at max degree 2. These numbers differ from the prior M10.1 parallel table because benchmark timings are machine/run/build specific and this refresh used a new run.
- **LLM-orchestrator baseline follow-up:** see [LLM Orchestrator Baseline Report](LLM_ORCHESTRATOR_BASELINE_REPORT.md) for the M10.2 live/manual Codex tick timing baseline.
- **Checkpoint/resume matched the straight deterministic result.** A Smoke checkpoint at tick 100 resumed for 150 ticks to the same final deterministic hash, `2ec6db6dd10db075`, as the straight 250-tick Smoke run. Automated tests also verify deterministic counters and final-state fields.
- **Battle completed as an additional one-trial scale point.** A 1,000-ship, 2,000-tick Battle run completed at **39,242.76 agent-ticks/sec**, **353,184.80 utility option evaluations/sec**, and hash `6cbe9adbdc6a37a2`. The repeated-trial public comparison mode remains Skirmish in this report.

## What is being measured?

Dominatus.RTSBenchmark measures runtime agent-orchestration throughput, not model inference throughput.

The simulated workload is a headless RTS-like combat loop:

- **One `AiAgent` per ship.** Smoke starts with 50 ships, Skirmish with 200 ships, and the one-trial Battle run with 1,000 ships.
- **HFSM ticks per live ship.** Each agent has a small behavior state machine and records action-state entries.
- **Real `Ai.Decide` usage.** Each live ship evaluates utility options such as `Advance`, `FocusFire`, `Retreat`, `Repair`, `Regenerate`, and `Idle`.
- **Per-agent blackboards.** Tactical summaries, local state, and decision inputs flow through blackboard reads and writes.
- **Mailbox/events.** Ships emit and receive events such as target spotted, repair requested, focus orders, ship destroyed, synapse lost, and ally under fire.
- **Deterministic action buffer.** Agents emit action intents. The benchmark sorts and resolves those actions deterministically after the decision phase.
- **SpatialGrid sensors and tactical banding.** Sensor work identifies relevant contacts and threat/support bands before decisions.
- **Dynamic sensor cadence.** Ships can skip expensive refreshes when their tactical situation permits it, while forced refreshes preserve behavior when damage/events/targets require fresh data.

The measured loop deliberately does **not** include:

- LLM calls or model inference;
- GPU work;
- rendering, windowing, or frame presentation;
- network I/O;
- external provider calls;
- disk I/O from JSON/CSV export.

This benchmark is therefore a CPU-side control-runtime benchmark. It demonstrates that high-frequency local behavior can be handled by a deterministic runtime, while LLMs can remain at semantic or high-level planning boundaries.

## Why this matters

Prompt-chain agent frameworks are designed around live LLM orchestration. Dominatus is a deterministic runtime/control system. This benchmark demonstrates work that should be runtime-driven rather than LLM-driven.

A damaged ship should not need a model call to decide whether to retreat.

For games, simulations, MMO servers, robotics-style control loops, and other multi-agent systems, the runtime needs to coordinate many stateful entities at high frequency. Dominatus.RTSBenchmark exercises that path directly: local state, utility decisions, event coordination, deterministic ordering, replayable hashes, and checkpoint/resume behavior.

## Environment

Fresh runs were collected in this environment:

| Field | Value |
|---|---:|
| OS | Ubuntu 24.04.4 LTS |
| Architecture | X64 |
| Runtime identifier | linux-x64 |
| Framework/runtime | .NET 10.0.8 |
| SDK used by commands | .NET SDK 10.0.300 |
| Processor count reported by benchmark | 2 |
| Build mode | Release |
| Target framework | net10.0 |
| Release `dotnet run` NativeAOT indicator | no |
| NativeAOT smoke executable indicator | yes |
| Command date | 2026-06-02 for M11 refresh; 2026-06-01 retained baseline sections |

Note: public benchmark claims should use Release or a NativeAOT-published executable, and should not mix Debug and Release numbers.

## Benchmark configuration

### Modes used

| Mode | Ships | Ticks | Purpose in this report |
|---|---:|---:|---|
| Smoke | 50 | 250 | Sanity, export, checkpoint/resume proof, NativeAOT smoke |
| Skirmish | 200 | 1,000 | Repeated-trial comparison mode |
| Battle | 1,000 | 2,000 | Optional one-trial scale point |

Armada was not run in this report.

### Sensor/cadence comparison configs

The Skirmish sensor/cadence comparison ran 5 sequential trials for each configuration:

1. `SpatialGrid + cadence` — spatial sensor grid with dynamic sensor cadence enabled.
2. `SpatialGrid no cadence` — spatial sensor grid with every live ship refreshing every tick.
3. `BroadScan no cadence` — broad scan baseline with every live ship refreshing every tick.

### Agent execution modes

The Skirmish agent-parallelism comparison ran 5 sequential trials for each execution mode at max degree 2:

1. `Sequential` — the baseline single-thread decision phase inside one benchmark simulation.
2. `LocalParallelDecision` — the benchmark-local parallel decision path enabled by `--parallel-agents`. It is lower overhead and specific to RTSBenchmark: each worker computes a ship action into a deterministic slot, then the benchmark merges those staged local actions in ship-id order before the existing deterministic action sort/resolution phase.
3. `CoreParallelRunner` — the generic Core staged parallel runner enabled by `--core-parallel-agents`. It uses staged context surfaces and deterministic merge semantics. In RTSBenchmark it is embedded in the decision phase with prepare steps disabled, because the benchmark owns cooldown, sensor, action resolution, event delivery, checkpoint, and hash phases. RTSBenchmark also enforces the safe subset by rejecting staged world blackboard writes, mailbox messages, actuations, or conflicts from this decision phase.

Important distinction:

- `--parallel-agents` parallelizes the **decision phase inside one benchmark simulation** through the benchmark-local fast path.
- `--core-parallel-agents` parallelizes the **decision phase inside one benchmark simulation** through the generic Core staged parallel runner.
- `--compare-agent-parallelism` now compares all three decision modes and reports hash stability plus hash equivalence versus sequential.
- `--parallel-trials` runs **independent benchmark instances concurrently**. It was not used for the public comparison numbers in this report.

## Results: Skirmish sensor/cadence comparison

All rows are Release `dotnet run`, `net10.0`, Skirmish mode, 5 sequential trials. Throughput uses median values unless otherwise stated.

| Config | Trials | Median agent-ticks/sec | Min/max agent-ticks/sec | Median option evals/sec | Median sensor ms | Median decision ms | Median skip rate | Hash stable | Relative median vs BroadScan |
|---|---:|---:|---:|---:|---:|---:|---:|---|---:|
| SpatialGrid + cadence | 5 | 130,862.42 | 30,947.69 / 140,054.19 | 1,177,761.79 | 296.05 | 303.82 | 56.9% | yes (`535c9b8e5f5d01e1`) | +36.5% |
| SpatialGrid no cadence | 5 | 89,302.03 | 83,647.59 / 97,952.49 | 803,718.27 | 572.18 | 352.63 | 0.0% | yes (`695d137f7de6d3f0`) | -6.9% |
| BroadScan no cadence | 5 | 95,889.61 | 89,113.79 / 103,531.36 | 863,006.53 | 470.51 | 303.71 | 0.0% | yes (`a2c4a3380da01967`) | baseline |

Observations:

- `SpatialGrid + cadence` was the best median configuration in this run set.
- Dynamic cadence skipped a median **56.9%** of sensor refreshes in Skirmish while retaining deterministic behavior for that configuration.
- The first `SpatialGrid + cadence` trial was a cold outlier at 30,947.69 agent-ticks/sec; median reporting avoids using that single noisy value as the claim.
- SpatialGrid alone was not faster than BroadScan in this Skirmish run; the documented win at this scale came from combining SpatialGrid with dynamic cadence.

## Results: parallel decision comparison

All rows are Release `dotnet run`, `net10.0`, Skirmish mode, 5 sequential trials. The comparison used `--compare-agent-parallelism --max-degree 2` on a machine where the benchmark reported 2 processors. Throughput uses median values from the generated JSON/CSV artifacts. Benchmark numbers are machine/run/build specific; this M11 refresh intentionally reports the fresh run even though it differs from the prior M10.1 parallel table.

| Config | Trials | Max degree | Median agent-ticks/sec | Median decisions/sec | Speedup vs sequential | Hash stable | Hash equivalent vs sequential | Notes |
|---|---:|---:|---:|---:|---:|---|---|---|
| Sequential | 5 | 2 | 76,942.79 | 692,485.14 | baseline | yes (`535c9b8e5f5d01e1`) | yes | Baseline single-thread decision phase; max degree is accepted by the comparison command but not used for worker decisions. |
| LocalParallelDecision | 5 | 2 | 74,149.66 | 667,346.98 | 0.96x | yes (`535c9b8e5f5d01e1`) | yes | Benchmark-local fast path; lower-overhead than the generic Core path but this run landed below the sequential median. |
| CoreParallelRunner | 5 | 2 | 68,625.18 | 617,626.66 | 0.89x | yes (`535c9b8e5f5d01e1`) | yes | Generic Core staged parallel runner embedded in the RTSBenchmark decision phase; safe-subset staged shared effects were zero in the Smoke sanity run. |

### Determinism proof

The Core runner's most important result is not raw speedup. It is deterministic equivalence. Sequential, benchmark-local parallel, and Core staged parallel modes produced the same deterministic hash for the same benchmark state and options in the tested RTSBenchmark safe subset.

The fresh M11 Skirmish comparison reported hash `535c9b8e5f5d01e1` for all three modes, with hash stability inside each 5-trial row and hash equivalence marked `yes` for both `LocalParallelDecision` versus sequential and `CoreParallelRunner` versus sequential. The Smoke Core runner sanity command also produced hash `2ec6db6dd10db075`, with `CoreParallelRunner` execution, 7,940 Core-parallel agents ticked, and zero staged world writes, mailbox messages, actuations, or conflicts.

This validates the staged compute / deterministic merge model for the tested safe subset: compute can happen in parallel while the simulation commits deterministic effects at tick boundaries.

## Checkpoint/resume proof

The checkpoint/resume proof used Smoke mode:

- Straight sanity/export run: 250 ticks, hash `2ec6db6dd10db075`.
- Checkpoint run: saved a checkpoint at tick 100 to `artifacts/rts-report-smoke.dsave`.
- Resume run: resumed from that checkpoint for 150 ticks.
- Resumed final result: 250 total ticks, hash `2ec6db6dd10db075`.

The CLI output provides the matching final hash. The automated test suite additionally verifies deterministic equality beyond the hash for checkpoint/resume, including agent ticks, decisions evaluated, actions emitted, events delivered, damage/repair/destroyed counters, winner, final ships, fleet power, and per-faction action/event counters.

## Optional Battle scale point

Battle was attempted and completed as a one-trial result:

| Mode | Ships | Ticks | Agent ticks | Option evals | Events delivered | Agent-ticks/sec | Option evals/sec | Events/sec | Hash |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| Battle | 1,000 | 2,000 | 1,031,227 | 9,281,043 | 11,672,264 | 39,242.76 | 353,184.80 | 444,181.35 | `6cbe9adbdc6a37a2` |

The Battle run demonstrates a larger real path, but it was not repeated in this pass. Skirmish remains the documented repeated-trial comparison mode for M10.1 claims.

## NativeAOT note

NativeAOT publish was attempted and succeeded for `linux-x64`:

```text
dotnet publish samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj -c Release -r linux-x64 -p:PublishAot=true --self-contained true -o artifacts/rtsbenchmark-native
```

The publish emitted expected AOT/trimming warnings around reflection-based `System.Text.Json` serialization/checkpoint code. No simulation behavior changes were made for this report.

A NativeAOT Smoke executable run completed with:

- NativeAOT indicator: yes;
- 250 ticks;
- 7,940 agent ticks;
- 71,460 option evaluations;
- 73,270.62 agent-ticks/sec;
- 659,435.58 option evaluations/sec;
- hash `2ec6db6dd10db075`.

This NativeAOT number is a Smoke sanity result, not the Skirmish comparison claim.

## Interpretation

- **Smoke is a correctness/tooling mode.** It is useful for sanity checks, JSON/CSV export, checkpoint/resume proof, and NativeAOT smoke validation. It is not the scale claim.
- **Skirmish is the first meaningful repeated-trial comparison mode.** It exercises 200 ships over 1,000 ticks and exposes sensor/decision/event costs.
- **SpatialGrid + dynamic cadence matters at Skirmish scale.** Cadence reduced median sensor refresh work enough to improve the best median throughput by 36.5% vs BroadScan no cadence.
- **Sensor remains important.** In Smoke, sensor was 47.1% of measured time. In Battle, sensor was 26.8% and event delivery became the top phase at 49.5%.
- **Decision remains meaningful.** Skirmish median utility option evaluations exceeded 1.17 million/sec in the best sensor/cadence configuration.
- **Parallel decision speedup is intentionally bounded.** Only the decision phase is parallelized; sensor work, action resolution, event delivery, checkpointing, and hashing remain controlled single-threaded phases. Deterministic equivalence is more important than raw speedup in this milestone.
- **`LocalParallelDecision` is a low-overhead benchmark-specific fast path.** It stages only RTSBenchmark ship actions and merges them deterministically; it is useful as a narrow optimized path but is not reusable Core infrastructure.
- **`CoreParallelRunner` is more general and may carry staging/merge overhead.** Its M11 result matters because it validates reusable Core staged context surfaces and deterministic merge semantics inside a real benchmark safe subset.
- **Amdahl's law still applies.** Only the parallel decision phase can benefit from worker concurrency in these modes; sensors, action resolution, event delivery, checkpointing, hashing, and final reporting remain outside the parallel decision section.
- **Battle shifts the hotspot.** At 1,000 ships, event delivery dominated the one-trial run. That is useful diagnostic evidence for future milestones, but M10.1 does not optimize or change behavior.

## What this proves

- Dominatus can run nontrivial stateful agents without LLM calls.
- Utility AI is enough for high-frequency local decisions.
- LLMs belong at semantic/high-level boundaries, not every tick.
- Dominatus can coordinate agents through blackboards, HFSMs, mailboxes/events, and deterministic action resolution.
- Dominatus can checkpoint/resume deterministic simulations to the same final result.
- Core staged surfaces are usable in a real benchmark safe subset.
- Parallel agent computation can preserve deterministic outcomes.
- Dominatus can move from benchmark-local parallelism toward generic safe parallel ticks.
- Dominatus can parallelize safe decision subsets without changing deterministic outcomes.
- The runtime model is suitable for MMO/RTS/server-simulation style workloads where compute happens in parallel and commits occur at tick boundaries.

## What this does not prove

- This is not a formal cross-vendor industry benchmark.
- This is not optimized against every competitor or framework.
- This is not a GPU, rendering, or graphics benchmark.
- This is not a model inference benchmark.
- This is not a networked MMO benchmark.
- This is not a claim of arbitrary authored-code parallel safety.
- `ctx.World` remains a live escape hatch in Core parallel safe-subset documentation.
- Transition and utility delegates still receive `AiWorld` and are not fully hardened for arbitrary parallel authorship.
- This is not full parallel sensor/action/event/checkpoint execution; only the decision phase is parallelized in the benchmark modes described here.
- This is not a replacement for future Core Parallel full-world safety hardening.
- This report does not publish repeated-trial Battle or Armada results.
- NativeAOT Skirmish comparison was not run in this pass; only a NativeAOT Smoke sanity run was collected.

## Reproduction commands

The following commands were run from the repository root.

### Smoke sanity run with JSON/CSV export

```bash
dotnet run -c Release --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --mode Smoke --no-checkpoints --json artifacts/rts-report-smoke.json --csv artifacts/rts-report-smoke.csv
```

Generated:

- `artifacts/rts-report-smoke.json`
- `artifacts/rts-report-smoke.csv`

### Skirmish sensor/cadence comparison

```bash
dotnet run -c Release --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --compare-sensor-cadence --mode Skirmish --trials 5 --json artifacts/rts-report-skirmish-sensor.json --csv artifacts/rts-report-skirmish-sensor.csv
```

Generated:

- `artifacts/rts-report-skirmish-sensor.json`
- `artifacts/rts-report-skirmish-sensor.csv`

### Skirmish agent parallelism comparison

```bash
dotnet run -c Release --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --compare-agent-parallelism --mode Skirmish --trials 5 --max-degree 2 --json artifacts/rts-report-skirmish-agent-parallel-m11.json --csv artifacts/rts-report-skirmish-agent-parallel-m11.csv
```

Generated:

- `artifacts/rts-report-skirmish-agent-parallel-m11.json`
- `artifacts/rts-report-skirmish-agent-parallel-m11.csv`

### Smoke Core runner sanity run

```bash
dotnet run -c Release --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --mode Smoke --core-parallel-agents --no-checkpoints --json artifacts/rts-report-smoke-core-parallel.json --csv artifacts/rts-report-smoke-core-parallel.csv
```

Generated:

- `artifacts/rts-report-smoke-core-parallel.json`
- `artifacts/rts-report-smoke-core-parallel.csv`

### Checkpoint/resume proof

```bash
dotnet run -c Release --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --mode Smoke --ticks 250 --checkpoint-at 100 --checkpoint-file artifacts/rts-report-smoke.dsave
```

```bash
dotnet run -c Release --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --resume-from artifacts/rts-report-smoke.dsave --resume-ticks 150
```

Generated:

- `artifacts/rts-report-smoke.dsave`

### Optional Battle one-trial run

```bash
dotnet run -c Release --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --mode Battle --no-checkpoints --json artifacts/rts-report-battle.json --csv artifacts/rts-report-battle.csv --progress-interval-seconds 10
```

Generated:

- `artifacts/rts-report-battle.json`
- `artifacts/rts-report-battle.csv`

### NativeAOT publish and Smoke run

```bash
dotnet publish samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj -c Release -r linux-x64 -p:PublishAot=true --self-contained true -o artifacts/rtsbenchmark-native
```

```bash
./artifacts/rtsbenchmark-native/Dominatus.RTSBenchmark --mode Smoke --no-checkpoints
```

Generated:

- `artifacts/rtsbenchmark-native/`

## Public claim template

> On Ubuntu 24.04.4 LTS, linux-x64, .NET 10.0.8, Release `net10.0`, with the benchmark reporting 2 processors, Dominatus.RTSBenchmark Skirmish reached **130,862.42 median agent-ticks/sec** over **5 sequential trials** using **SpatialGrid + dynamic sensor cadence**, with stable deterministic hashes and no GPU/rendering/network/model inference in the measured loop.

Optional Core parallel claim:

> In the same environment, RTSBenchmark M11 compared `Sequential`, `LocalParallelDecision`, and `CoreParallelRunner` decision modes at max degree 2 over **5 Skirmish trials**. All three produced deterministic hash `535c9b8e5f5d01e1`; `LocalParallelDecision` reached **74,149.66 median agent-ticks/sec** (**0.96x** versus sequential) and `CoreParallelRunner` reached **68,625.18 median agent-ticks/sec** (**0.89x** versus sequential), demonstrating deterministic hash equivalence for the tested safe subset rather than a raw-speedup claim.

## Appendix: raw artifact notes

The generated JSON/CSV/checkpoint/native publish artifacts were used to write this report and left under `artifacts/` locally. They are not intended to be committed as part of this documentation refresh. The M11 refresh generated only the `*-m11` Skirmish parallel artifacts and the Smoke Core parallel artifacts; older artifact names below are retained because the report also preserves the prior sensor/cadence, checkpoint/resume, Battle, and NativeAOT sections.

Artifact paths from this pass:

- `artifacts/rts-report-smoke.json`
- `artifacts/rts-report-smoke.csv`
- `artifacts/rts-report-skirmish-sensor.json`
- `artifacts/rts-report-skirmish-sensor.csv`
- `artifacts/rts-report-skirmish-agent-parallel-m11.json`
- `artifacts/rts-report-skirmish-agent-parallel-m11.csv`
- `artifacts/rts-report-smoke-core-parallel.json`
- `artifacts/rts-report-smoke-core-parallel.csv`
- `artifacts/rts-report-smoke.dsave`
- `artifacts/rts-report-battle.json`
- `artifacts/rts-report-battle.csv`
- `artifacts/rtsbenchmark-native/`

Only the markdown report and documentation index/pointer are part of the intended repository change.
