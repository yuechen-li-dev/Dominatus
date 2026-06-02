# LLM Orchestrator Baseline Report

_Run date: 2026-06-02. Measurement status: live/manual Codex self-measurement over 5 RTS-style action-decision trials._

## Executive summary

- This report measures a rough live/manual LLM orchestration tick baseline.
- The task is one RTSBenchmark-style action decision for a single ship.
- The baseline is intentionally generous: one agent, one decision, no tool call, no memory retrieval, no multi-agent manager.
- Dominatus performs this class of ordinary tactical decision locally through deterministic utility AI at CPU speed.
- In this rough self-measurement, one Codex-mediated RTS-style action decision took **2.04 seconds median**, or about **0.490 decisions/sec**.
- Dominatus.RTSBenchmark Skirmish runs **130,862.42 median agent-ticks/sec** locally in Release `net10.0` with no model inference in the measured loop.
- The resulting throughput gap is about **267,010x** against the sequential Skirmish median.

Dominatus does not make prompt chains faster. Dominatus removes prompt chains from the hot path.

## What is being compared?

### Dominatus.RTSBenchmark

- Local deterministic runtime.
- Many stateful agents.
- Utility decisions over the benchmark action set.
- No model calls in the measured loop.

The current comparison number from the RTS benchmark report is **130,862.42 median agent-ticks/sec** for Skirmish, Release `net10.0`, using SpatialGrid + dynamic sensor cadence. The same report records **1,177,761.79 median utility option evaluations/sec** for that run and **142,049.52 median agent-ticks/sec** for benchmark-local parallel decision mode.

### LLM baseline

- One model-mediated action decision.
- One agent.
- Prompt in, answer out.
- No tools, retrieval, memory, manager agent, or multi-agent coordination.

The measurement is the observed wall-clock time for Codex, acting as the LLM orchestrator in the current session, to choose and justify one action from the supplied tactical prompt.

## Why this is not apples-to-apples

- The LLM runs on remote or provider infrastructure, not inside the same local deterministic 2-core CPU loop.
- The LLM decision includes service, session, prompt-processing, and output-generation latency.
- Dominatus local decisions do not perform semantic language generation.
- That category mismatch is the point: model-mediated control loops are bounded by model-call latency, while Dominatus removes model calls from the ordinary high-frequency decision path.

LLMs remain valuable for semantic judgment. They are a poor fit for every ordinary control tick.

## Task prompt

The trials used this exact prompt:

```text
You are an LLM acting as an RTS agent orchestrator for one tick.

Ship:
faction: Dominion
class: RailgunDestroyer
hull: 42%
shield: 18%
cooldown: ready
current target: Collective SynapseCruiser-17
position: midline

Tactical summary:
immediate threat: NeedleDrone-42 at close range
best attack target: SynapseCruiser-17, high value, damaged to 37%
nearby ally: RepairTender-03, in range but under pressure
local threat score: high
relevant enemy contacts: 5
relevant ally contacts: 3
command focus order: focus SynapseCruiser-17

Available actions:
Advance
FocusFire
Retreat
RepairAlly
LaunchDrone
Regenerate
HoldFormation
Idle

Task:
Choose the next action for this ship for this tick. Briefly justify it.

Respond in this exact format:
Action: <one action>
Justification: <one sentence>
```

## Measurement method

Option A was used as a live/manual Codex self-measurement in the current session.

For each trial, a shell timestamp was recorded immediately before presenting the decision prompt to Codex's reasoning path, Codex selected the action and one-sentence justification, and a second shell timestamp was recorded immediately after the decision was made. The elapsed seconds are the difference between those timestamps.

This method is intentionally rough. It measures the interactive Codex agent path available in this environment, including session/tool-turn overhead around the model reasoning step. It is still a generous baseline for LLM-as-orchestrator designs because the decision had no tool call, no retrieval, no memory operation, no manager agent, and no multi-agent coordination.

## Trial results

| Trial | Chosen action | Elapsed seconds | Usable? | Notes |
| ----- | ------------- | --------------: | ------- | ----- |
| 1 | FocusFire | 2.72 | Yes | Railgun was ready and the command focus order identified a damaged high-value SynapseCruiser. |
| 2 | FocusFire | 2.11 | Yes | Same prompt; prioritized removing the damaged coordination target despite local threat. |
| 3 | FocusFire | 2.04 | Yes | Same prompt; ready weapon and focus order outweighed retreat for this tick. |
| 4 | FocusFire | 1.98 | Yes | Same prompt; selected immediate high-value fire rather than repositioning. |
| 5 | FocusFire | 1.95 | Yes | Same prompt; selected the command-focused damaged target while the shot was available. |

## Summary statistics

- Min seconds: **1.95**.
- Median seconds: **2.04**.
- Mean seconds: **2.16**.
- Max seconds: **2.72**.
- LLM decisions/sec from median: **0.490**.
- Dominatus.RTSBenchmark Skirmish median agent-ticks/sec: **130,862.42**.
- Estimated throughput gap: **267,010x**.

Calculation:

```text
LLM decisions/sec = 1 / 2.04 = 0.490
throughput gap = 130,862.42 / 0.490 = 267,010x
```

Using the unrounded median, the computed gap is **267,009.76x**.

## Interpretation

Even a minimal LLM orchestration tick is orders of magnitude slower than Dominatus utility decisions. The measured Codex tick made a reasonable tactical choice, but it did so at human-interaction/model-call scale rather than deterministic runtime scale.

This does not mean LLMs are bad. It means LLMs belong at semantic boundaries: summaries, analysis, coding, dialogue, ambiguous judgment, and review. Ordinary high-frequency control decisions belong in deterministic runtime state.

A damaged frigate should not need a model call to decide whether to retreat.

## Limitations

- Manual/live LLM timing is noisy.
- This is a rough Codex self-measurement, not a formal provider benchmark.
- The timing includes interactive session/tool-turn overhead around the model reasoning step.
- This is not a formal LangGraph implementation benchmark.
- This is not measuring model quality.
- This is not measuring multi-step tool workflows.
- This is not measuring memory retrieval, manager agents, or multi-agent framework overhead.
- This is a rough category baseline, not a cross-vendor benchmark.

## Relation to LangGraph/CrewAI-style orchestration

Frameworks built around model-mediated orchestration are valuable for semantic workflows, but if each agent tick requires a live model step, their tick rate is bounded by model latency. Dominatus uses deterministic runtime orchestration and calls models only when semantic intelligence is needed.

This report does not benchmark LangGraph, CrewAI, or any other agent framework directly.

## Public claim template

```text
In a rough LLM-orchestrator baseline, one model-mediated RTS-style action decision took 2.04 seconds median, or about 0.490 decisions/sec. In contrast, Dominatus.RTSBenchmark Skirmish runs 130,862.42 stateful agent ticks/sec locally in Release net10.0 with no model inference in the measured loop. This illustrates the category gap between live model-mediated orchestration and deterministic runtime orchestration.
```
