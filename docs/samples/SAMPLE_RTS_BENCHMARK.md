# Dominatus.RTSBenchmark M0 design and benchmark contract

`Dominatus.RTSBenchmark` is a planned headless RTS-like CPU benchmark for Dominatus agent orchestration. It is inspired by the way large RTS battles are often used to stress a machine, but it is not a game benchmark, renderer benchmark, GPU benchmark, or live-LLM benchmark.

Professional contract phrasing:

> Dominatus.RTSBenchmark measures deterministic agent-orchestration throughput: ships make utility decisions, exchange events, and emit actions over thousands of ticks without invoking LLMs.

The planned sample path is:

```text
samples/Dominatus.RTSBenchmark
```

M0 is intentionally a design/contract milestone. It defines the architecture, simulation rules, benchmark modes, metrics, test plan, and API fit after inspecting current Dominatus APIs and samples. It does not claim performance results and does not implement the full benchmark.

## Premise

In the distant future, humanity and allied LLM/AI systems have formed the Dominion. As the Dominion takes its first steps toward the stars, it encounters the Collective: an alien species fused with its own AI creations into organic/machine hybrid fleets and a Borg-like hivemind.

The benchmark simulates asymmetric fleet battles between two doctrines:

- **The Dominion**: human + AI cooperation, flexible doctrine, mixed fleets, command ships, repair/logistics, long-range fire control, and adaptive task groups.
- **The Collective**: organic/machine hivemind, swarm pressure, synapse coordination, regenerative hulls, sacrifice tactics, adaptive focus fire, and bio-machine boarding/spore effects.

The output should feel like a headless RTS battle report. It is not a graphics application and has no rendering path.

## Design thesis

Dominatus should prove it can run many stateful agents quickly. This benchmark targets the runtime work that a game/simulation-like orchestration loop performs at high tick rates:

- many agents;
- many ticks;
- utility decisions;
- state updates;
- mailbox/event delivery;
- actuator-like action emission/resolution;
- deterministic replay/result hashing;
- CPU throughput.

This benchmark is intentionally not an LLM benchmark. It demonstrates a high-frequency deterministic orchestration workload that LLM-centered prompt-chain orchestrators are not designed to run.

## Hard non-goals

M1 must not add or require:

- rendering, windows, GPU work, shaders, or frame presentation;
- live LLM calls, `Llm.Call`, OpenRouter, OpenAI, Anthropic, Semantic Kernel planners, API keys, or cassettes;
- network access;
- pathfinding;
- real physics;
- an ECS rewrite;
- Dominatus.Core changes;
- a parallel scheduler;
- full MMO simulation scope;
- huge data files;
- a NuGet package.

No external dependencies should be introduced unless they are already present and unavoidable. The preferred M1 implementation should depend on `Dominatus.Core`, `Dominatus.OptFlow`, and possibly `Dominatus.UtilityLite` only.

## Current Dominatus API inspection

M0 inspected `Dominatus.Core`, `Dominatus.OptFlow`, `Dominatus.UtilityLite`, TinyTown, ParallelModuleWorkflow, FishTank, SimConsole, and relevant core tests. The important findings are below.

### Agents and world ticking

`AiWorld` owns a list of `AiAgent` instances, assigns ids on `Add`, stores public `AgentSnapshot` data, exposes a shared world blackboard, advances the clock, ticks the actuator if it is tickable, then ticks each agent sequentially. `AiAgent.Tick` expires its blackboard TTL values and ticks its HFSM brain.

This is a good fit for an M1 single-threaded benchmark loop. It also means M1 should not claim parallel scheduler performance.

### Utility decisions and `Ai.Decide`

`Dominatus.OptFlow.Ai.Decide` emits a `Decide` step containing a `DecisionSlot`, `UtilityOption` list, and `DecisionPolicy`. `UtilityOption` uses `Consideration`, a fast `Func<AiWorld, AiAgent, float>` score that clamps to `0..1`.

Existing samples already use this pattern for repeated runtime decisions:

- FishTank uses `Ai.Decide` to select prey behavior (`Flee`, `SeekFood`, `Wander`) and predator behavior (`Hunt`, `Wander`).
- SimConsole uses `Ai.Decide` for guard behavior selection.
- TinyTown creates one `AiAgent` per townie and repeatedly decides among action states such as eating, sleeping, working, chatting, and idling.

M1 can use real `Ai.Decide` for ship behavior, provided ship state is kept in blackboards or world-accessible arrays and each action state yields back frequently enough for thousands of ticks.

### Blackboard state

Each agent has a local blackboard, and `AiWorld` has a world blackboard. TinyTown uses local blackboards for per-agent needs, location, action, and profile ids. This maps naturally to ship state such as class, faction, hull, shield/carapace, cooldown, target id, current action, and threat flags.

For M1 performance and determinism, the benchmark should keep the authoritative fleet arrays benchmark-local and mirror only the minimal decision inputs/outputs into agent blackboards. This avoids turning blackboard key/value storage into the measured physics/data layer.

### Mailbox and events

`AiWorld.Mail` is a default mailbox that routes typed messages to an agent's per-agent `AiEventBus`; `Broadcast` enumerates public snapshots and sends messages to matching recipients. `AiEventBus` is typed and optimized for “wait for next event of type T” using per-type append-only buckets and cursors. Core tests cover publishing and consuming events with `Ai.Event<T>`.

This is suitable for benchmark combat events when the goal is to measure event delivery in the Dominatus style. However, `Broadcast` currently does a predicate scan over public snapshots. M1 should use it deliberately for command/synapse/focus-order events, not for every damage application if that would turn the benchmark into a broadcast-predicate benchmark.

### Actuator-like commands

`ActuatorHost` is a typed command dispatcher with handlers, policies, immediate completions, and deferred completions. `Ai.Act` emits an `Act` step containing an `IActuationCommand`. FishTank registers handlers such as `SteerTowardCommand`, `SteerAwayCommand`, and `WanderCommand` to translate agent intent into movement state.

This is an excellent semantic match for “agents emit actions, a host resolves them later.” It is not necessarily the right primary path for M1 scoring because `ActuatorHost` measures command dispatch abstraction, handler lookup, policy evaluation, and completion event plumbing. The benchmark's core score should primarily measure agent decision throughput and deterministic action/event simulation, not I/O abstraction overhead.

### Existing sample lessons

- **TinyTown** proves one `AiAgent` per entity is a normal Dominatus shape for small deterministic simulations. It also demonstrates utility decisions as the simulation director while LLM calls are optional actors outside the high-frequency loop.
- **FishTank** proves a game/simulation loop can use Dominatus utility decisions and actuator commands for non-LLM movement intent, but it is a MonoGame rendering sample and therefore not a model for benchmark I/O or output.
- **SimConsole** is a compact text simulation and useful as a style reference for a console runner.
- **ParallelModuleWorkflow** is not a simulation benchmark, but it reinforces that host-level orchestration and deterministic fake integrations should be explicit. Its LLM-specific pieces should not be used by RTSBenchmark.

## Required M0 answers

### 1. Can M1 use real `Ai.Decide` for every ship?

Yes for Smoke, Skirmish, and a modest Battle target, with one important constraint: M1 should keep the behavior nodes small and hot. Every ship can run a root utility decision over actions such as `Advance`, `FocusFire`, `Retreat`, `RepairAlly`, `ScreenHighValue`, `LaunchDrone`, `Regenerate`, `HoldFormation`, and `Idle`.

The honest unknown is upper-end throughput for thousands of ships because current samples are not large CPU benchmarks. M1 should start with Option A-small and report actual results before promising Armada-scale rates.

### 2. Can M1 use one `AiAgent` per ship without absurd setup cost?

Probably yes for M1 Smoke/Skirmish and for a modest Battle run. `AiAgent` construction requires an `HfsmInstance`; `AiWorld.Add` assigns ids and stores a public snapshot. TinyTown and FishTank already create independent agents per simulated entity.

The risk is not conceptual setup cost; it is per-agent HFSM/iterator/blackboard overhead at 1,000+ ships. M1 should measure it directly instead of avoiding it. If 5,000-ship Armada mode is too slow, M2 can add squad/group agents without invalidating M1.

### 3. Is there an existing event/mailbox pattern suitable for combat events?

Yes. The default mailbox and typed event bus are suitable for events like `TargetSpotted`, `AllyUnderFire`, `RepairRequested`, `SynapseLost`, and `CommandFocusOrder`. M1 should use explicit typed records for these events and count deliveries.

Damage and repair resolution should remain in the deterministic resolution phase. Events should report or coordinate facts; they should not mutate hull directly from inside receiving agents.

### 4. Is there an existing actuator-like pattern suitable for action emission/resolution?

Yes. `Ai.Act` plus `IActuationCommand`/`ActuatorHost` is the existing pattern. FishTank already uses typed movement commands behind handlers.

For RTSBenchmark, action records such as `AttackAction`, `MoveAction`, `RepairAction`, `LaunchDroneAction`, and `BroadcastAction` map naturally to command-like data.

### 5. Should M1 use actual `ActuatorHost`, or a benchmark-local action buffer?

M1 should use a benchmark-local deterministic action buffer for the primary score, and optionally include a small side metric or later mode that routes through `ActuatorHost`.

Rationale:

- The primary benchmark thesis is deterministic agent-orchestration throughput.
- RTS action resolution is not I/O; it is local simulation state mutation.
- Sorting and resolving action records deterministically is closer to RTS simulation architecture than treating every shot or movement step as a side-effect command completion.
- Using `ActuatorHost` for every ship action would measure policy/handler/completion overhead that is important for real actuators but not central to the headless RTS contract.

The benchmark-local action buffer should still mirror the Dominatus actuation pattern: agents emit intent records; the simulation resolves them later. This keeps the benchmark honest without conflating local simulation with external side effects.

### 6. What exact metrics are realistic?

M1 can realistically measure:

- `TicksSimulated`;
- `InitialShips`;
- `FinalShips`;
- `AgentTicks` accumulated as ships processed per tick;
- `DecisionsEvaluated`;
- `ActionsEmitted`;
- `EventsDelivered`;
- `DamageEvents`;
- `RepairEvents`;
- `DestroyedShips`;
- `ElapsedWallClock`;
- `AgentTicksPerSecond`;
- `DecisionsPerSecond`;
- `ActionsPerSecond`;
- `EventsPerSecond`;
- `DeterminismHash`;
- `Winner`;
- `RemainingFleetPower`.

The primary score should be:

```text
Score = AgentTicksPerSecond
```

Secondary rates should be decisions/sec, actions/sec, and events/sec. A composite score can be explored later, but M1 should keep the score transparent.

### 7. What should be left for M2/M3?

M2 should consider:

- squad/group-agent mode for larger fleets;
- optional `ActuatorHost` comparison mode;
- profiling and allocation reduction after M1 measurements exist;
- richer combat events and doctrine coordination;
- deterministic replay file output if in-memory hash is not enough.

M3 should consider:

- parallel scheduler experiments if Dominatus runtime support exists;
- larger Armada tuning;
- a stable benchmark corpus/output contract for CI or release comparisons;
- optional visualization of saved reports outside the benchmark process, still not part of the measured run.

## Architecture options evaluated

### Option A — full Dominatus agent-per-ship

Each ship is an `AiAgent` with blackboard state and an HFSM behavior node using `Ai.Decide`.

Pros:

- most honest Dominatus benchmark;
- directly measures many stateful agents;
- exercises HFSM, blackboard, utility decisions, events, and emitted actions;
- matches the benchmark thesis.

Cons:

- setup/runtime overhead may cap ship counts;
- M1 will need careful hot-loop design;
- high-level `Ai.Act`/`ActuatorHost` should probably not be in the primary score path.

### Option B — squad/ship-group agent model

Each squad is an `AiAgent` that controls multiple ships internally.

Pros:

- more scalable;
- realistic RTS command hierarchy;
- useful for very large fleets.

Cons:

- less direct as a per-ship agent benchmark;
- hides individual ship decisions inside benchmark-local code;
- better as an M2 scaling mode after Option A has a baseline.

### Option C — benchmark-local tight loop using Dominatus decision components

Use real utility/action logic but not full agent machinery per ship.

Pros:

- fastest and easiest;
- easiest to make deterministic;
- useful as a microbenchmark or control case.

Cons:

- less honest as a Dominatus benchmark;
- bypasses too much of `AiAgent`, HFSM, mailbox, and event behavior;
- risks becoming a custom RTS simulation benchmark rather than a Dominatus benchmark.

### M1 recommendation

Start M1 with **Option A-small**: one `AiAgent` per ship for Smoke, Skirmish, and Battle, using real `Ai.Decide`, real blackboards for decision-facing state, real mailbox/event delivery for coordination events, and a benchmark-local deterministic action buffer for primary action resolution.

If Option A-small cannot reach acceptable Battle throughput, M1 should still ship honestly with Smoke/Skirmish and record the bottleneck. M2 should then add Option B for squad/group scaling. Option C should remain a possible internal control path, not the headline benchmark.

## Factions and ship classes

M1 should define fixed data for each class. Initial numbers can be tuned, but they must remain deterministic and transparent.

### Dominion classes

| Class | Role | Baseline behavior | Suggested stat emphasis |
| --- | --- | --- | --- |
| Scout Frigate | Sensor/screening | Detect targets, screen high-value ships, retreat when threatened | high speed, high sensor range, fragile hull/shield, low damage |
| Missile Corvette | Burst damage | Fire cooldown-heavy missiles at valuable targets | medium range, medium damage, high cooldown, medium speed |
| Railgun Destroyer | Long-range direct fire | Focus high-value visible targets | high damage, long range, slower tracking/cooldown |
| Carrier | Drone launch / command support | Launch drones when enemy is in range; retreat when threatened | high strategic value, long support range, low direct speed |
| Repair Tender | Repair allies | Repair damaged allies, avoid direct combat | low combat power, repair amount/range, fragile |
| Command Cruiser | Coordination aura / focus-fire boost | Broadcast focus targets, boost nearby allies | high strategic value, command radius, moderate durability |

### Collective classes

| Class | Role | Baseline behavior | Suggested stat emphasis |
| --- | --- | --- | --- |
| Needle Drone | Swarm attacker | Attack nearest vulnerable target; low self-preservation | fast, expendable, short range, low hull |
| Spore Frigate | Area denial / morale pressure | Pressure clusters and high-value support | medium durability, medium range, debuff/spore event weight |
| Synapse Cruiser | Command/synapse coordination | Stay near swarm center; broadcast focus target | command radius, high value, moderate durability |
| Regenerator | Biological repair support | Regenerate damaged allies; retreats less than Dominion tender | repair/regeneration, medium durability |
| Harvester | Sustain/energy leech | Pressure shielded/high-value targets | leech damage, medium range, moderate speed |
| Hive Ark | Heavy capital / swarm anchor | Advance slowly, anchor formation, absorb punishment | very high hull/carapace, slow speed, high role weight |

### Simple stats

Each class should have:

- `Hull`;
- `Shield` for Dominion or `Carapace/Regen` for Collective;
- `Damage`;
- `Range`;
- `Speed`;
- `Cooldown`;
- `SensorRange`;
- `RoleWeight`;
- `Faction`.

M1 does not need pathfinding, real physics, projectile travel, formations beyond simple behavior scores, or detailed weapons simulation.

## Deterministic simulation model

M1 should use a simple 2D abstract battlefield:

- position as `Vector2` or two floats;
- distance checks for sensors, weapon range, repair range, command radius, and synapse radius;
- simple movement steps toward or away from selected targets;
- deterministic damage/repair application;
- deterministic action sorting before resolution;
- no randomness by default;
- if randomness is introduced later, it must be seeded, deterministic, counted, and included in the result hash.

Ship destruction should mark ships dead and exclude them from later decision/action phases. Avoid removing from arrays during the tick; compact only between ticks or not at all.

## M1 tick flow

Each simulation tick should run these phases in this order:

1. **Sensor phase**
   - Update visible enemies and nearby allies.
   - Populate minimal per-agent blackboard facts: nearest visible enemy, vulnerable ally, threat level, own hull fraction, command/synapse flags.

2. **Decision phase**
   - Each alive ship's `AiAgent` chooses an action using utility.
   - Candidate actions: `Advance`, `FocusFire`, `Retreat`, `RepairAlly`, `ScreenHighValue`, `LaunchDrone`, `Regenerate`, `HoldFormation`, `Idle`.
   - Increment `AgentTicks` and `DecisionsEvaluated`.

3. **Action emission phase**
   - Ships emit action records into a benchmark-local action buffer.
   - Agents do not directly mutate hull, position, or cooldown in this phase.

4. **Resolution phase**
   - Sort actions deterministically by tick, action priority, faction, actor id, target id, and action type.
   - Apply damage, repair, movement, cooldown changes, drone launches, and death marks.
   - Increment action, damage, repair, and destroyed counters.

5. **Event phase**
   - Publish typed events such as `ShipDestroyed`, `AllyUnderFire`, `TargetSpotted`, `RepairRequested`, `SynapseLost`, and `CommandFocusOrder`.
   - Use Dominatus mailbox/event delivery where the event is meant to be consumed by agents.
   - Count `EventsDelivered`.

6. **Metrics phase**
   - Update fleet power, remaining ship counts, per-faction destroyed counts, and rolling counters.

7. **Checkpoint reporting phase**
   - Every N ticks, write a deterministic summary line.

This order prevents agents from mutating authoritative combat state while other agents are still deciding, which is important for deterministic replay and fair scoring.

## Utility decision examples

### Dominion examples

- **Scout Frigate**
  - If no contacts are visible, prefer `Advance`/scout.
  - If threatened, prefer `Retreat`.
  - If an enemy capital is visible, emit or request `TargetSpotted`.

- **Repair Tender**
  - Score `RepairAlly` highly when a nearby ally has low hull.
  - Score `Retreat` highly when directly threatened.
  - Avoid frontline `FocusFire` unless no support action exists.

- **Command Cruiser**
  - Prefer `FocusFire` on the highest-value visible enemy.
  - Emit `CommandFocusOrder` for nearby allies.
  - Avoid direct combat when hull is low.

- **Carrier**
  - Prefer `LaunchDrone` when enemies are in support range and cooldown is ready.
  - Prefer `Retreat` when threatened by nearby attackers.

### Collective examples

- **Needle Drone**
  - Prefer swarming the nearest vulnerable target.
  - Keep self-preservation scores low so drones attack instead of retreating in most cases.

- **Synapse Cruiser**
  - Stay near the swarm center.
  - Broadcast a focus target.
  - Generate `SynapseLost` consequences when destroyed.

- **Regenerator**
  - Repair/regenerate damaged allies.
  - Retreat less aggressively than a Dominion Repair Tender.

- **Hive Ark**
  - Advance slowly.
  - Anchor formation.
  - Absorb punishment and provide high fleet-power weight.

## Metrics and scoring contract

M1 final reports should include:

- `TicksSimulated`;
- `InitialShips`;
- `FinalShips`;
- `AgentTicks`;
- `DecisionsEvaluated`;
- `ActionsEmitted`;
- `EventsDelivered`;
- `DamageEvents`;
- `RepairEvents`;
- `DestroyedShips`;
- `ElapsedWallClock`;
- `AgentTicksPerSecond`;
- `DecisionsPerSecond`;
- `ActionsPerSecond`;
- `EventsPerSecond`;
- `DeterminismHash`;
- `Winner`;
- `RemainingFleetPower`.

Primary CPU score:

```text
Score = AgentTicksPerSecond
```

Secondary scores:

```text
DecisionsPerSecond
ActionsPerSecond
EventsPerSecond
```

A possible future composite score is:

```text
(AgentTicks + DecisionsEvaluated + ActionsEmitted + EventsDelivered) / elapsed seconds
```

Do not use the composite as the primary M1 score; it is less transparent and can reward event spam.

## Determinism hash

M1 should compute a stable result hash over deterministic final and/or checkpoint state, including at least:

- mode name;
- seed, if any;
- total ticks simulated;
- per-ship alive/dead state;
- final hull/shield/carapace values after deterministic quantization;
- final positions after deterministic quantization;
- action/event/damage/repair/destroyed counters;
- winner and remaining fleet power.

The hash should not include wall-clock time or machine-specific values.

## Benchmark modes

| Mode | Ships | Ticks | Purpose |
| --- | ---: | ---: | --- |
| Smoke | 50 | 250 | Fast validation and tests |
| Skirmish | 200 | 1,000 | Normal quick benchmark |
| Battle | 1,000 | 2,000 | Main CPU benchmark target |
| Armada | 5,000 | 5,000 | Manual benchmark mode only |

Armada must not run in tests or normal CI.

## Console output contract

The M1 console app should print:

- benchmark name;
- mode;
- faction setup;
- checkpoint reports every N ticks;
- final battle report;
- CPU score;
- determinism hash.

Checkpoint example:

```text
[T+0500] Dominion 71% fleet power | Collective 64% fleet power | destroyed D:42 C:118 | decisions 500,000 | actions 183,211 | events 41,902
```

Final report example:

```text
=== Dominatus.RTSBenchmark ===
Mode: Battle
Winner: Dominion
Ticks simulated: 2,000
Ships initial: 1,000
Ships remaining: 416
Agent ticks: 2,000,000
Decisions evaluated: 2,000,000
Actions emitted: 812,441
Events delivered: 144,220
Elapsed: 0.82s
Score: 2.43M agent-ticks/sec
Determinism hash: 9F23A1C4
```

The exact numbers above are illustrative only. M0 makes no performance claims.

## Sample and test project shape for M1

Preferred M1 project layout:

```text
samples/Dominatus.RTSBenchmark/
  Dominatus.RTSBenchmark.csproj
  Program.cs
  BenchmarkMode.cs
  BenchmarkRunner.cs
  BenchmarkMetrics.cs
  BattleReport.cs
  DeterminismHasher.cs
  Simulation/
    BattleSimulation.cs
    ShipClassDefinition.cs
    ShipState.cs
    ShipAction.cs
    ShipEvents.cs
    FleetFactory.cs
    UtilityScorers.cs
    ShipAgentFactory.cs

tests/Dominatus.RTSBenchmark.Tests/
  Dominatus.RTSBenchmark.Tests.csproj
  RtsBenchmarkSmokeTests.cs
  RtsBenchmarkDeterminismTests.cs
  RtsBenchmarkUtilityTests.cs
```

If M1 needs to keep the sample tiny, `Simulation/` files can be collapsed initially, but the public contract should remain clear.

## M1 testing strategy

Recommended tests:

- Smoke benchmark completes.
- Determinism: same mode/seed produces the same final hash.
- Dominion and Collective both emit actions.
- Metrics counters are non-zero for Smoke.
- Checkpoint output contains expected fields.
- Utility decisions produce expected action under focused scenarios:
  - damaged ship retreats;
  - repair tender repairs ally;
  - Collective drone attacks instead of retreating;
  - synapse cruiser boosts/focuses.
- Benchmark code does not reference `Dominatus.Llm.OptFlow`, `Llm.Call`, provider clients, Semantic Kernel, or network APIs.
- Armada mode is excluded from tests.

Suggested M1 commands:

```bash
dotnet build samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0
dotnet test tests/Dominatus.RTSBenchmark.Tests/Dominatus.RTSBenchmark.Tests.csproj --framework net10.0
dotnet test Dominatus.slnx
```

## M0 decision: no skeleton project yet

M0 does not add a placeholder project. The design contract is now specific enough that a skeleton would add little value unless it contains real simulation contracts and tests. Adding a stub that only prints planned modes would create solution and test-surface churn without proving the important API decisions.

M1 should add the sample and tests together so the project shape, CLI behavior, determinism hash, and Smoke run can be validated in one coherent step.

## M1 implementation prompt recommendations

A good M1 prompt should ask for:

1. Create `samples/Dominatus.RTSBenchmark` and `tests/Dominatus.RTSBenchmark.Tests`.
2. Implement Option A-small: one `AiAgent` per ship using real `Ai.Decide`.
3. Use a benchmark-local action buffer for primary action resolution.
4. Use real mailbox/event delivery for coordination events and count deliveries.
5. Implement Smoke, Skirmish, Battle, and manual Armada modes.
6. Implement deterministic tick phases and result hash.
7. Implement the M1 test list above.
8. Verify there are no LLM, network, rendering, Semantic Kernel, or GPU dependencies.
9. Run the sample build/run, benchmark tests, and `dotnet test Dominatus.slnx`.

## Outcome

M0 outcome: **A — success**.

The design contract exists; it defines the premise, factions, deterministic simulation model, metrics, modes, output, and testing strategy; it explicitly excludes LLM/GPU/network work; it recommends an M1 architecture after inspecting current Dominatus APIs; and it adds documentation links without runtime behavior changes.

## M1 implementation: runnable headless RTS benchmark

M1 adds the runnable sample at:

```text
samples/Dominatus.RTSBenchmark
```

and the test project at:

```text
tests/Dominatus.RTSBenchmark.Tests
```

The sample implements the M0 Option A-small path: one `AiAgent` per ship, a small HFSM rooted in a real `Ai.Decide` utility decision, per-agent blackboard inputs mirrored from the authoritative simulation state, and a benchmark-local deterministic action buffer for primary action resolution.

### How to run

Default Smoke run:

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0
```

Manual Battle run:

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --mode Battle
```

Focused override run:

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --mode Smoke --ships 100 --ticks 500 --checkpoint-interval 100
```

CLI arguments:

- `--mode Smoke|Skirmish|Battle|Armada` selects the benchmark size. Default is `Smoke`.
- `--ships N` overrides the mode ship count.
- `--ticks N` overrides the mode tick count.
- `--checkpoint-interval N` changes checkpoint cadence.
- `--no-checkpoints` suppresses checkpoint lines.

### Modes

- `Smoke`: 50 ships, 250 ticks; quick correctness and local smoke path.
- `Skirmish`: 200 ships, 1,000 ticks; medium local run.
- `Battle`: 1,000 ships, 2,000 ticks; manual benchmark run.
- `Armada`: 5,000 ships, 5,000 ticks; manual-only stress option and not used by tests.

### What M1 measures

The primary score is:

```text
AgentTicksPerSecond = AgentTicks / elapsed wall-clock seconds
```

Secondary rates are decisions/sec, actions/sec, and events/sec. M1 also reports ticks simulated, initial/final ships, destroyed ships, damage events, repair events, delivered coordination events, final fleet powers, winner/draw, checkpoints, and a deterministic hash.

The determinism hash intentionally excludes wall-clock time and derived rate values. It includes the mode, simulated ticks, ship counts, final per-ship alive/hull/shield/position/cooldown state, deterministic counters, winner, and final fleet power.

### Tick flow implemented

Each tick runs deterministic phases:

1. Sensor phase mirrors decision-facing facts into each ship agent blackboard.
2. Decision phase ticks each alive ship's `AiAgent` once and uses real `Ai.Decide` over RTS actions.
3. Action emission records selected ship intents in the benchmark-local action buffer.
4. Resolution sorts actions deterministically and applies movement, focus fire, repairs/regeneration, cooldowns, and destruction.
5. Event phase uses the real Dominatus mailbox/event bus path for coordination events such as focus orders, target sightings, repair requests, ally-under-fire notices, ship destruction, and synapse loss.
6. Metrics/checkpoint phases update fleet power and optional checkpoint output.

### What M1 implements vs future work

M1 implements deterministic headless CPU orchestration for Dominion and Collective fleets with utility decisions, real mail/event delivery, action records, combat/repair resolution, checkpoints, CLI output, and focused tests.

M1 deliberately does **not** implement rendering, windows, GPU work, shaders, live LLM calls, `Llm.Call`, OpenAI/OpenRouter/Anthropic providers, Semantic Kernel, network access, pathfinding, real physics, a parallel scheduler, server endpoints, an ECS rewrite, or large data files.

Future milestones can add squad/group-agent modes, optional `ActuatorHost` comparison paths, richer doctrine coordination, replay files, profiling/alloc reductions, or a non-measured visualization of saved reports.

### Validation commands

```bash
dotnet build samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0
dotnet test tests/Dominatus.RTSBenchmark.Tests/Dominatus.RTSBenchmark.Tests.csproj --framework net10.0
dotnet test Dominatus.slnx
```

## M1 outcome

M1 outcome: **A — success**.

The benchmark sample now exists and runs; Smoke mode completes; each ship has an `AiAgent`; ship actions are selected through real `Ai.Decide`; combat resolves through a deterministic benchmark-local action buffer; Dominatus mailbox/event delivery is used for coordination events and counted; repeated runs produce stable deterministic hashes; metrics, score, checkpoints, and final reports are printed; focused utility tests pass; and the sample avoids LLM, GPU, network, provider, and rendering dependencies.

## M2 phase timing and hotspot diagnostics

M2 extends the runnable `samples/Dominatus.RTSBenchmark` sample with measurement-first diagnostics. It does not optimize the simulation or change `Dominatus.Core`; its purpose is to answer where benchmark time is currently spent before selecting future work.

The result API now reports a measured simulation window and per-phase timings:

- `MeasuredSimulationTime` is the sum of measured benchmark phases. It covers the simulation loop plus final metrics/hash work that is explicitly timed, not process startup or console report formatting.
- `PhaseTimings` contains one `RtsBenchmarkPhaseTiming` record per phase with `Name`, raw `ElapsedTicks`, converted `Elapsed`, and `PercentOfMeasuredRuntime`.
- `ElapsedWallClock` remains the outer benchmark stopwatch for the run, including the measured simulation work and final metrics/hash timing before console report formatting.

The standard phase names are:

- `Cooldown` — per-ship cooldown decrement.
- `Sensor` — authoritative ship state mirrored into decision-facing blackboards, including nearest-enemy and vulnerable-ally scans.
- `Decision` — real per-ship `AiAgent.Tick` / `Ai.Decide` work plus action intent emission.
- `ActionResolution` — deterministic action sorting and combat/repair/movement resolution.
- `EventDelivery` — mailbox sends and delivered coordination events.
- `Metrics` — final fleet-power/winner aggregation.
- `Checkpoint` — checkpoint line construction and writes when checkpoints are enabled.
- `Hashing` — deterministic hash finalization.

The final console report always includes a compact hotspot line such as:

```text
Hot path: Sensor 61.3%, Decision 22.1%, ActionResolution 9.4%
```

The hotspot summary is built by sorting phases by elapsed time descending and formatting the top three percentages with invariant-culture one-decimal formatting. Percentages are derived from local timings and are therefore machine/run dependent, but the shape of the string is deterministic for a given set of phase measurements.

M2 also adds diagnostic counters to help distinguish Dominatus runtime machinery from benchmark-local RTS simulation work:

- `SensorPairsChecked`
- `UtilityOptionsEvaluated`
- `ActionsSorted`
- `MailboxEventsSent`
- `MailboxEventsDelivered`
- `CheckpointsWritten`
- `BlackboardReads`
- `BlackboardWrites`

Timings and diagnostics are intentionally excluded from `DeterminismHash`. The hash remains a replay/result fingerprint over deterministic simulation inputs, final ship state, winner, fleet power, and core deterministic outcome counters. Runtime measurements are expected to vary by machine, load, JIT state, and operating system scheduling, so including them would make the determinism hash useless.

Interpretation guidance:

- If `Sensor` dominates, a future milestone should evaluate spatial partitioning or lower-cost visibility queries.
- If `Decision` dominates, future work should inspect blackboard/key access and `Ai.Decide`/HFSM overhead.
- If `EventDelivery` dominates, future work should evaluate event batching or narrower delivery fanout.
- If `ActionResolution` dominates, future work should tune the benchmark-local action buffer and deterministic sort path.

M2 deliberately stops at measurement. It does not add spatial partitioning, a parallel scheduler, Core changes, new ship classes, rendering, network calls, LLM calls, BenchmarkDotNet, or CI performance thresholds.
