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

## M3 tactical threat/support banding

M3 changes the tactical sensor and utility-decision model from “nearest broad fact” inputs to compact local tactical summaries. The benchmark still performs a deterministic pair scan in the sensor phase, so this is not yet a spatial-partition optimization. The goal is to make each ship's local utility inputs more realistic and smaller: nearby contacts matter most, sensor-band contacts provide context, and out-of-range ships do not influence local action scoring.

### Distance bands

Every live ship classifies every other live contact into one of four deterministic `TacticalDistanceBand` values:

| Band | Meaning | Threshold |
| --- | --- | --- |
| `Immediate` | The contact can shoot this ship or this ship can shoot the contact now. | `distance <= own weapon range` or, for enemies, `distance <= enemy weapon range` |
| `Near` | The contact is local and likely to matter soon. | `distance <= own sensor range * 0.5` |
| `Sensor` | The contact is visible through normal tactical sensors/command awareness. | `distance <= own sensor range` |
| `OutOfRange` | The contact is not locally relevant. | `distance > own sensor range` |

Out-of-range contacts are counted for diagnostics but ignored for local utility summaries. They do not become immediate threats, attack targets, repair targets, or support context.

### Tactical summaries instead of broad facts

The sensor phase now computes one compact `TacticalSummary` per ship and mirrors only scalar/id fields into the agent blackboard:

- `ImmediateThreatId`
- `BestAttackTargetId`
- `BestRepairTargetId`
- `HighestValueVisibleEnemyId`
- `LocalThreatScore`
- `LocalSupportScore`
- relevant enemy/ally counts
- booleans such as `HasImmediateThreat` and `HasRepairTarget`

Raw contact lists are intentionally not stored in blackboards. Authoritative ship state remains in the benchmark fleet arrays, while blackboards receive the small tactical summary needed by utility scorers.

### Threat and priority scoring

Threat scoring combines distance band, enemy class damage/role weight, weapon-range relation, cooldown readiness, and own hull vulnerability. The documented band weights are:

```text
Immediate = 1.00
Near      = 0.55
Sensor    = 0.20
OutOfRange= 0.00
```

Attack priority favors closer targets, high-value classes, damaged targets, immediate/near contacts, and command-focus orders. Command/synapse ships, carriers/hive arks, and repair/regenerator units receive additional role priority. Repair priority is ally-only and favors nearby damaged high-value allies; damaged allies outside sensor range are ignored.

### Doctrine profile bridge

M3 adds a small `DoctrineProfile` bridge for future higher-level orchestration:

- Dominion has higher `PreserveHighValueShips` and `RepairPriority`, so it values logistics and protection.
- Collective has higher `Aggression` and `FocusCommandTargets`, so it favors pressure and command-target focus over preservation.

The profiles are intentionally simple and deterministic. M4 or later milestones can extract doctrine into richer fleet-level orchestration without changing the M3 blackboard contract.

### Decision scorer behavior

Utility scorers now read tactical-summary blackboard values rather than broad global/nearly-global facts:

- `Retreat` rises with `LocalThreatScore`, low hull, and `HasImmediateThreat`; Needle Drones still prefer attack unless critically damaged.
- `FocusFire` uses `BestAttackTargetId`, target band, cooldown readiness, and attack priority.
- `RepairAlly` uses `BestRepairTargetId` and `LocalSupportScore` for repair-capable ships.
- `Advance` is strongest when there is no immediate threat and enemies exist only in the sensor band or no local enemies are present.
- `HoldFormation` and `ScreenHighValue` respond to nearby support/command context and de-emphasize holding under immediate threat.
- Command/synapse focus orders still flow through events and can boost a visible target's attack priority.

### Tactical band diagnostics

M3 extends the result/report with tactical-band counters:

- `RelevantEnemyContacts`
- `RelevantAllyContacts`
- `IgnoredOutOfRangeContacts`
- `ImmediateThreatContacts`
- `NearContacts`
- `SensorBandContacts`
- `RelevantContactsPerAgentTick`
- `IgnoredContactsPerSensorPair`

The final report includes a `Tactical band diagnostics` section. Interpret a high ignored-contact ratio as evidence that many scanned pairs are not locally meaningful. That can be expected in wide battles and is one reason M4 should consider spatial partitioning if sensor work remains a hot path or ship counts scale poorly.

### Not a spatial grid yet

M3 deliberately keeps the pair scan and `SensorPairsChecked` counter. It improves the model and blackboard inputs but does not add a grid, quadtree, broadphase, parallel scheduler, real pathfinding, or physics. If future smoke/skirmish/battle reports show sensor time growing with fleet size, M4 should use these counters to justify and validate a spatial partition or command-layer broadphase.

## M4 spatial grid sensor acceleration

M4 keeps the M3 tactical model intact and changes only how each ship discovers local tactical contacts. Instead of making every alive ship scan every other alive ship for every sensor tick, the benchmark can rebuild a deterministic uniform grid from alive ship positions and query nearby cells for candidate contacts.

### Sensor modes

`RtsBenchmarkOptions.SensorMode` selects the candidate-discovery path:

- `SpatialGrid` is the default M4 mode.
- `BroadScan` remains available for comparison, diagnostics, and regression tests.

The CLI accepts the same choice:

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --mode Smoke --sensor SpatialGrid --no-checkpoints
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --mode Smoke --sensor BroadScan --no-checkpoints
```

### Cell size behavior

The default spatial cell size is deterministic: it is the maximum `SensorRange` across the ship class definitions. At M4 this resolves to `72`, from the Dominion `CommandCruiser` sensor profile. A custom positive value may be supplied through `RtsBenchmarkOptions.SpatialCellSize` or `--spatial-cell-size`.

Each ship maps to a cell with `floor(x / cellSize), floor(y / cellSize)`, which intentionally supports negative coordinates. A sensor query visits neighboring cells within `ceil(sensorRange / cellSize)`. With the default cell size, most classes visit a one-cell radius around their current cell. The grid can return candidates outside exact sensor range; M3 tactical banding still computes exact distances and rejects out-of-range contacts.

### Deterministic ordering

Grid cells are visited in deterministic coordinate order: increasing cell X, then increasing cell Y. Ship ids within cells are sorted when the grid is rebuilt, and query results are sorted by ship id before tactical scoring. Tactical tie-breaking continues to prefer higher score, then lower ship id where scores are equal. This means SpatialGrid mode does not depend on dictionary enumeration order.

### Spatial diagnostics

The final report now includes:

- sensor mode;
- spatial cell size;
- broad equivalent pairs (actual refreshed ships multiplied by other living ships; with dynamic cadence disabled this is `aliveShips * (aliveShips - 1)` per tick);
- spatial candidate pairs before exact distance filtering, excluding self;
- pairs skipped by the grid;
- maximum populated cells seen in any tick;
- total spatial cell visits across all ship queries.

`SensorPairsChecked` means exact ship-pair distance checks performed by the tactical sensor phase after dead/self filtering. In `BroadScan`, it should match the broad equivalent pair count for ships that actually refreshed sensors. In `SpatialGrid`, it should match the spatial candidates passed to exact tactical distance filtering for refreshed ships.

### Interpreting Sensor phase changes

SpatialGrid should reduce candidate pairs when fleets are spread across multiple cells. It is still a simple per-tick full rebuild uniform grid, not a quadtree, loose octree, incremental interest manager, or parallel broad-phase. In small Smoke runs the added grid bookkeeping can offset the reduced pair count, so M4 reports both timings and structural pair counts. Performance should be read together with `Spatial candidate pairs`, `Pairs skipped by grid`, and the Sensor phase percentage.

### Still not included

M4 does not add rendering, GPU work, networking, live LLM calls, provider dependencies, BenchmarkDotNet, pathfinding, physics, a parallel scheduler, or a complicated spatial tree.

### Future work

- Tune cell sizes and formation geometry for larger battlefield spreads.
- Avoid full per-tick grid rebuilds if movement patterns warrant incremental updates.
- Add squad/group agent modes to reduce per-ship tactical duplication.
- Add a parallel tick scheduler as a separate benchmark milestone.

## M5 allocation and decision hot-path diagnostics

M5 extends the runnable RTS benchmark with diagnostics only. It intentionally does **not** optimize Dominatus Core, the spatial grid, gameplay balance, or the action/event logic. The purpose is to decide where M6 work should focus after measuring the current hot paths.

### Allocation diagnostics

`RtsBenchmarkResult` now reports allocation and GC counters for the measured single-threaded run:

- `AllocatedBytes`
- `BytesPerAgentTick`
- `BytesPerDecision`
- `Gen0Collections`
- `Gen1Collections`
- `Gen2Collections`

The benchmark captures `GC.GetAllocatedBytesForCurrentThread()` and `GC.CollectionCount(...)` before the measured simulation phases and after metrics/hash computation, before final report formatting. This deliberately excludes console report formatting from the allocation measurement. `GC.GetAllocatedBytesForCurrentThread()` only observes allocations on the current thread; that is acceptable for M5 because the RTS benchmark remains single-threaded.

Interpret `BytesPerAgentTick` as a coarse pressure indicator: it divides current-thread allocated bytes by the number of ship-agent ticks actually processed. It should be compared across the same mode and options, not treated as a universal constant. `BytesPerDecision` divides by the benchmark's utility decision-option evaluations and helps separate broad agent tick volume from utility scoring pressure.

### Decision diagnostics

The M5 result includes explicit hot-path counters for the per-agent decision loop:

- `AgentTickCalls`
- `HfsmTicks`
- `DecideSteps`
- `UtilityOptionsEvaluated`
- `UtilityOptionsSelected`
- `ActionStatesEntered`
- emitted action counters for idle, retreat, focus fire, repair, advance, launch drone, regenerate, and hold formation/screening

It also derives:

- `UtilityOptionsPerAgentTick`
- `AgentTicksPerDecisionSecond`
- `DecisionsPerAgentTick`
- `ActionsPerAgentTick`

These counters are intended to distinguish runtime agent/HFSM overhead from utility option scoring and action emission pressure. `UtilityOptionsEvaluated` remains the benchmark's option-throughput counter, while `UtilityOptionsSelected` shows one selected utility result per processed agent tick.

### Blackboard access diagnostics

M5 counts benchmark-controlled blackboard access without modifying Core:

- `DecisionBlackboardReads`
- `DecisionBlackboardWrites`
- `SensorBlackboardWrites`

Sensor writes are counted where the benchmark mirrors tactical summaries into each agent's blackboard. Decision reads are counted by benchmark helper paths used around utility scoring and action target selection. Decision writes are counted for benchmark-controlled decision/event writes, such as focus-order target mirroring. Core blackboard internals are not instrumented, so these values should be read as benchmark-sample access pressure, not a complete Core blackboard profiler.

### Event diagnostics

Mailbox pressure is reported as:

- `MailboxEventsSent`
- `MailboxEventsDelivered`
- `TargetSpottedEvents`
- `RepairRequestedEvents`
- `CommandFocusOrderEvents`
- `ShipDestroyedEvents`
- `SynapseLostEvents`
- `AllyUnderFireEvents`
- `EventsPerAgentTick`
- `EventsPerAction`

Event-type counters are useful for identifying whether mailbox pressure is dominated by routine command/spotting chatter, repair requests, or destruction/synapse events.

### Action diagnostics and phase timing

M5 separates deterministic action sorting from action resolution when it is low-risk to do so. `PhaseTimings` now includes `ActionSort` in addition to `ActionResolution`, and the result reports:

- `ActionSortBatches`
- `MaxActionsInTick`
- `AverageActionsPerTick`
- `ActionsSorted`

This makes it possible to see whether action ordering is a meaningful cost separate from applying movement, damage, repair, and regeneration effects.

### Determinism hash policy

The determinism hash continues to include deterministic battle state and deterministic gameplay counters. It excludes elapsed timings, allocation bytes, GC counts, and derived diagnostic rates. Timings and allocations can vary between otherwise equivalent runs, so including them would make the hash a machine/runtime noise detector rather than a replay determinism check.

### What M5 can reveal

M5 should provide enough evidence to choose an M6 direction:

- reduce allocations in the benchmark sample hot loop;
- cache or reshape utility options if option evaluation dominates;
- reduce blackboard mirror writes or benchmark-controlled blackboard reads;
- reduce event chatter if mailbox sent/delivered counts dominate;
- optimize action sorting if `ActionSort` and action batch counters justify it;
- revisit sensor candidate discovery only if spatial counters still dominate;
- inspect Core HFSM or `Ai.Decide` hot paths only if sample-level overhead is insufficient to explain the measured cost.

M5 deliberately stops at measurement. Any optimization should be a separate milestone with before/after evidence from these counters.

## M6 dynamic sensor cadence

M6 optimizes the benchmark-local tactical sensor phase by applying the same cadence principle used elsewhere in Dominatus: work that does not need to be recomputed every tick should be refreshed at a deterministic urgency-dependent interval. The RTS sensor pass is benchmark perception work, not an HFSM transition scan, so M6 keeps the cadence state in `BattleSimulation` rather than changing Core scheduling.

### Concept

Sensor cost is approximately:

```text
ships × refresh frequency × candidate count
```

M4 reduced candidate count with `SpatialGrid`. M6 reduces refresh frequency. A ship that skips its sensor refresh reuses the previous `TacticalSummary`, mirrors that stale-but-bounded summary back to its blackboard, and avoids both BroadScan and SpatialGrid candidate lookup for that ship on that tick.

`SensorMode` and dynamic cadence are orthogonal:

- `SensorMode = BroadScan|SpatialGrid` chooses candidate discovery for ships that refresh this tick.
- `EnableDynamicSensorCadence` chooses whether every living ship refreshes every tick or can reuse its last tactical summary.

The default options are deterministic:

- `EnableDynamicSensorCadence = true`
- `MinSensorCadenceTicks = 1` when unset
- `MaxSensorCadenceTicks = 12` when unset

Both min and max must be at least one tick, and max must be greater than or equal to min when both are supplied.

### Cadence rules

After each refresh, the benchmark chooses the next per-ship sensor cadence from the newly computed tactical summary:

- immediate threat present: 1 tick;
- near contacts or significant local threat: 2 ticks;
- sensor-band contacts or relevant support contacts: 4 ticks;
- no relevant contacts: 8 ticks.

Role and state modifiers then clamp that base cadence:

- scouts, command cruisers, and synapse cruisers refresh at least every 3 ticks;
- carriers and hive arks refresh at least every 4 ticks;
- repair-capable ships refresh at least every 3 ticks when a repair target is known, otherwise every 4 ticks;
- needle drones refresh every 1–2 ticks while engaged and at least every 4 ticks otherwise;
- damaged ships below 35% hull and ships with a live current target refresh at least every 2 ticks;
- the final value is clamped to the configured min/max bounds.

### Invalidation rules

A refresh is forced when any of these deterministic local conditions apply:

- the previous tactical summary is missing;
- the current tick has reached the ship's next scheduled refresh;
- the ship took hull/shield damage since its last sensor refresh;
- the ship's current target is missing or dead;
- the ship received a sensor-relevant event: `TargetSpotted`, `AllyUnderFire`, `CommandFocusOrder`, Collective `SynapseLost`, or `RepairRequested` for repair-capable ships.

M6 counts damage, event, and target invalidation separately in the result so forced refresh pressure can be distinguished from ordinary cadence expiration.

### Diagnostics and interpretation

The final report includes a **Sensor cadence diagnostics** section:

- dynamic cadence enabled/disabled;
- refreshes performed;
- refreshes skipped;
- skip rate;
- stale summary uses;
- forced refreshes, broken down by damage/event/target invalidation;
- average selected cadence;
- cadence selection counts for immediate, near, sensor-band, and idle cases.

`BroadSensorPairsEquivalent` now represents the actual broad-scan-equivalent pair work for ships that refreshed sensors, not the theoretical work if every living ship had refreshed every tick. Spatial diagnostics similarly reflect actual refreshed-ship candidate queries.

The determinism hash includes deterministic cadence counters (`SensorRefreshesPerformed`, `SensorRefreshesSkipped`, forced refreshes, and cadence selection counters). It still excludes timing, allocation, and GC data.

### CLI

Additional flags:

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --mode Smoke --sensor SpatialGrid --no-checkpoints

dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --mode Smoke --sensor SpatialGrid --no-checkpoints --disable-sensor-cadence

dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --min-sensor-cadence 1 --max-sensor-cadence 12
```

Use the dynamic and disabled runs together to compare score, top phases, sensor phase time/percent, skip rate, and average cadence. Do not treat any one run as a guaranteed speedup claim; compare on the same machine and options.

### Future work

- Tune cadence by doctrine and fleet composition.
- Expose a per-class cadence table instead of hard-coded benchmark defaults.
- Integrate event-driven refresh queues if the benchmark later gets a broader event scheduler.
- Revisit cadence interaction with a future parallel scheduler.

## M7 repeated trial comparison tooling

M7 adds an in-process comparison runner for reducing single-run timing noise before deciding whether a benchmark configuration is materially better or worse. A single RTSBenchmark report remains useful for inspecting phases, diagnostics, and deterministic outcomes, but wall-clock rates can move between runs because of JIT warmup, CPU scheduling, background work, garbage collection, and cache state. The comparison runner executes repeated independent trials, keeps checkpoint output suppressed by default, and summarizes stable descriptive statistics without enforcing CI performance thresholds.

### Sensor cadence comparison

The built-in comparison is the sensor cadence comparison. It runs the same mode across multiple configurations:

1. `SpatialGrid + cadence` — SpatialGrid broad-phase sensors with dynamic sensor cadence enabled.
2. `SpatialGrid no cadence` — SpatialGrid broad-phase sensors with every ship refreshed every tick.
3. `BroadScan no cadence` — optional BroadScan baseline without dynamic cadence.

The comparison reports median, mean, min, and max agent-ticks/sec, plus median sensor phase time, median decision phase time, mean allocated bytes, median sensor refresh skip rate, and determinism hash stability for each configuration. Median is usually the first number to inspect because it is less sensitive to a single unusually slow or fast trial than the mean.

The runner does not claim statistical significance and does not assert exact speedups. It only describes the observed trial set so a human can decide whether more investigation is warranted.

### Sequential versus parallel trials

Sequential comparison is the primary single-thread benchmark comparison mode. It runs each trial one after another in the current process and is the mode to use when comparing per-trial `AgentTicksPerSecond` between configurations.

Parallel comparison is optional and launches independent benchmark instances concurrently with a configurable maximum degree of parallelism. It measures batch/multicore throughput behavior for independent benchmark trials, not the primary single-thread score. Parallel mode intentionally introduces CPU contention and can reduce individual trial rates, so its output is labeled with a note and should be interpreted separately from sequential results.

### Determinism hash stability

Each trial still produces the deterministic benchmark hash. M7 summaries list the distinct hashes observed for a configuration and report whether they are stable. Repeated deterministic trials for the same configuration should produce one hash. If a summary reports unstable hashes, treat that as a correctness or determinism investigation before interpreting performance differences.

### CLI examples

Run a five-trial sequential Smoke comparison:

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --compare-sensor-cadence --mode Smoke --trials 5
```

Run a three-trial Skirmish comparison with parallel trial batches:

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --compare-sensor-cadence --mode Skirmish --trials 3 --parallel-trials
```

Limit parallel trial concurrency explicitly:

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --compare-sensor-cadence --mode Smoke --trials 3 --parallel-trials --max-degree-of-parallelism 2
```

Print compact per-trial lines before the summaries:

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --compare-sensor-cadence --mode Smoke --trials 5 --trial-details
```

Omit the BroadScan baseline when you only want the two SpatialGrid cadence configurations:

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --compare-sensor-cadence --mode Smoke --trials 5 --no-broadscan-baseline
```

### Output shape

Comparison output begins with a clearly labeled header:

```text
=== Dominatus.RTSBenchmark Comparison ===
Type: Sensor cadence
Mode: Smoke
Trials: 5
Execution: Sequential
```

Each configuration then receives a summary block containing the primary rate distribution, key phase medians, allocation mean, skip-rate median, and hash stability. The final `Best median` line names the configuration with the highest median `AgentTicksPerSecond` within that observed trial set and gives its margin over the next best median. This line is descriptive only; it is not a performance threshold and does not imply statistical significance.

## M7.1 non-Smoke scale comparison report

M7 Smoke comparisons were useful for validating the repeated-trial runner, but Smoke is too small to judge sensor optimization direction. Smoke uses 50 ships over 250 ticks, so a full broad scan can be cheap enough that SpatialGrid rebuild/query bookkeeping and dynamic-cadence state maintenance compete with, or exceed, the work they save. M7.1 therefore treats Smoke as a correctness and tooling scale, and uses Skirmish as the first meaningful scale for sensor-mode claims.

These results were captured on the local agent machine during this documentation update. They are machine-specific, Debug-build `dotnet run` measurements and are not formal published benchmark numbers. Public performance claims should use a documented machine, runtime, build configuration, power profile, and command line. Armada was not run; it remains manual-only.

### Commands run

Sequential Skirmish comparison:

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --compare-sensor-cadence --mode Skirmish --trials 5
```

Sequential Battle comparison attempt:

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --compare-sensor-cadence --mode Battle --trials 3
```

The Battle comparison produced no summary output after about 6 minutes and 44 seconds and was interrupted as not reasonable for this reporting pass. That means Battle remains deferred rather than reported.

Optional parallel Skirmish comparison:

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --compare-sensor-cadence --mode Skirmish --trials 5 --parallel-trials --max-degree-of-parallelism 2
```

### Sequential Skirmish results

| Mode     | Config                 | Trials | Median agent-ticks/sec | Min agent-ticks/sec | Max agent-ticks/sec | Median decisions/sec | Sensor ms median | Decision ms median | Allocated bytes mean | Allocated bytes median | Skip rate median | Hash stable |
| -------- | ---------------------- | -----: | ---------------------: | ------------------: | ------------------: | -------------------: | ---------------: | -----------------: | -------------------: | ---------------------: | ---------------: | ----------- |
| Skirmish | BroadScan no cadence   |      5 |              29,119.04 |           27,462.93 |           29,945.74 |           262,071.38 |         1,636.12 |             623.77 |        164,530,548.80 |       not printed by CLI |             0.0% | yes         |
| Skirmish | SpatialGrid no cadence |      5 |              31,162.99 |           27,737.54 |           32,848.86 |           280,466.93 |         1,411.00 |             644.70 |        170,379,515.20 |       not printed by CLI |             0.0% | yes         |
| Skirmish | SpatialGrid + cadence  |      5 |              39,911.94 |           21,273.63 |           44,342.24 |           359,207.50 |           809.96 |             683.87 |        133,315,936.00 |       not printed by CLI |            56.9% | yes         |

Best median configuration: `SpatialGrid + cadence`, with a reported +28.1% median advantage over the next best median configuration in this run.

The dominant measured phase in the sequential comparison is Sensor for all three configurations. Sensor median time was about 2.6x Decision for `BroadScan no cadence`, about 2.2x Decision for `SpatialGrid no cadence`, and about 1.2x Decision for `SpatialGrid + cadence`. Dynamic cadence substantially reduced Sensor median time and allocation mean in this trial set, while Decision time remained in the same general range.

### Optional parallel Skirmish results

Parallel trials measure throughput for independent benchmark instances under CPU contention, not the primary single-thread score. They are included only as an optional batch-throughput observation.

| Mode     | Execution                 | Config                 | Trials | Median agent-ticks/sec | Min agent-ticks/sec | Max agent-ticks/sec | Median decisions/sec | Sensor ms median | Decision ms median | Allocated bytes mean | Allocated bytes median | Skip rate median | Hash stable |
| -------- | ------------------------- | ---------------------- | -----: | ---------------------: | ------------------: | ------------------: | -------------------: | ---------------: | -----------------: | -------------------: | ---------------------: | ---------------: | ----------- |
| Skirmish | Parallel, max degree 2    | BroadScan no cadence   |      5 |              27,167.15 |           26,357.53 |           30,038.32 |           244,504.32 |         1,715.09 |             686.96 |        164,533,897.60 |       not printed by CLI |             0.0% | yes         |
| Skirmish | Parallel, max degree 2    | SpatialGrid no cadence |      5 |              28,296.81 |           27,177.47 |           29,356.05 |           254,671.31 |         1,552.53 |             662.84 |        170,381,419.20 |       not printed by CLI |             0.0% | yes         |
| Skirmish | Parallel, max degree 2    | SpatialGrid + cadence  |      5 |              39,841.10 |           18,590.72 |           41,241.25 |           358,569.87 |           851.88 |             665.71 |        133,323,102.40 |       not printed by CLI |            56.9% | yes         |

Best median configuration in the optional parallel run: `SpatialGrid + cadence`, with a reported +40.8% median advantage over the next best median configuration. Because this mode intentionally runs independent trials concurrently, the spread should not be mixed with the sequential comparison when making single-thread claims.

### Battle result

Battle mode was attempted with 3 sequential trials, but no comparison summary was printed after about 6 minutes and 44 seconds. The run was stopped instead of waiting for a potentially long Debug-build comparison. M7.1 therefore does not answer Battle-scale crossover with completed data. A later pass should either run Battle in a documented Release configuration, add progress output for long comparisons, or run fewer Battle trials only if that is accepted as a separate measurement shape.

### Interpretation

For the required Skirmish comparison, SpatialGrid did start beating BroadScan: `SpatialGrid no cadence` had a 31,162.99 median agent-ticks/sec result versus 29,119.04 for `BroadScan no cadence`, while also reducing median Sensor time from 1,636.12 ms to 1,411.00 ms. This is a modest Skirmish-scale crossover observation, not a universal claim.

Dynamic sensor cadence helped materially at Skirmish scale in this run. `SpatialGrid + cadence` reached 39,911.94 median agent-ticks/sec, reduced median Sensor time to 809.96 ms, reported a 56.9% median sensor skip rate, and had the lowest reported mean allocation. The low minimum trial for `SpatialGrid + cadence` shows that timing noise still exists, so repeated trials remain necessary.

Sensor work remains the top hot path among the fields exposed by the comparison summary. Decision median time is still significant, but the largest differences between configurations come from Sensor median time and sensor refresh skip rate. The comparison CLI did not print the complete per-run hot-path summary or median allocated bytes, so those fields are intentionally marked as not printed rather than inferred.

Determinism hashes were stable for every Skirmish configuration in both sequential and optional parallel comparisons. Hash stability supports interpreting the rate differences as performance/timing differences rather than divergent simulation outcomes.

Current practical throughput on this machine for Skirmish sequential comparisons was about 29.1K agent-ticks/sec for `BroadScan no cadence`, 31.2K for `SpatialGrid no cadence`, and 39.9K for `SpatialGrid + cadence`. The current recommended benchmark mode for public claims is still a repeated sequential comparison, preferably Skirmish or larger, with the machine/runtime/build configuration documented. Smoke should be described as a quick correctness and tooling check rather than evidence for or against sensor optimizations.

What remains noisy or unknown:

- Battle-scale results are still unknown from this pass because the Debug-build comparison did not complete in a reasonable time.
- The CLI prints mean allocated bytes but not median allocated bytes, even though the comparison data model tracks the median internally.
- The comparison summary exposes Sensor and Decision phase medians, but not the full single-run hot-path summary for each configuration.
- These results were captured from one machine and one run set; they should be rerun before making release-note or public benchmark claims.

## M8 tick-boundary checkpoint/resume proof

M8 adds an app-level checkpoint/resume proof for `Dominatus.RTSBenchmark`. The benchmark can now run to a completed tick boundary, save deterministic simulation truth into the existing Dominatus save container, reload it, reconstruct agents/HFSMs deterministically, and continue from the next tick. The proof target is:

1. run `N` ticks straight;
2. run `K` ticks, save a checkpoint, load it, and run `N-K` more ticks;
3. compare deterministic hash, deterministic counters, winner, fleet power, and final ship counts.

The checkpoint deliberately does **not** serialize compiler-generated C# iterator/enumerator objects. RTSBenchmark saves the spellbook rather than pickling the goblin: explicit ship state, deterministic counters, checkpoint report lines, sensor-cadence state, tactical summaries, focus targets, and the HFSM active state path are persisted. On load the sample rebuilds the fleet arrays, `AiWorld`, one `AiAgent` per ship, blackboards, sensor cadence state, and HFSM paths from that explicit truth. Fresh node enumerators are created by the normal HFSM restore path.

Checkpoints are valid only at tick boundaries after cooldown, sensor, decision, action-sort, action-resolution, mailbox/event delivery, and optional checkpoint-report phases have completed. The action buffer is scratch state and is not durable. There is no mid-tick checkpointing and no live external actuation, LLM, network, rendering, GPU, or provider state in the checkpoint.

The save file uses the existing Dominatus persistence infrastructure:

- Core `SaveFile`/`SaveChunk` binary container;
- `DominatusSave.CreateCheckpointChunks` and `DominatusSave.ReadCheckpointChunks`;
- an app-specific `ISaveChunkContributor` chunk;
- chunk id `rtsbenchmark.state`;
- chunk format `application/vnd.dominatus.rtsbenchmark.checkpoint+json`;
- chunk/checkpoint version `1`.

Timing, GC, and allocation measurements are intentionally not deterministic checkpoint state. A resumed run has its own wall-clock/allocation diagnostics, while equivalence tests compare deterministic hash, deterministic counters, final state summaries, and sensor/spatial counters.

Programmatic APIs:

```csharp
var checkpoint = RtsBenchmarkRunner.RunToCheckpoint(options, stopAfterTicks: 100);
RtsBenchmarkCheckpointStore.SaveToFile(checkpoint, "artifacts/rts-smoke.dsave");

var loaded = RtsBenchmarkCheckpointStore.LoadFromFile("artifacts/rts-smoke.dsave");
var result = RtsBenchmarkRunner.ResumeFromCheckpoint(loaded, additionalTicks: 150);
```

CLI examples:

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --mode Smoke --ticks 250 --checkpoint-at 100 --checkpoint-file artifacts/rts-smoke.dsave
```

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- --resume-from artifacts/rts-smoke.dsave --resume-ticks 150
```

This implements the app-level proof anticipated by `docs/user/PERSISTENCE_CHECKPOINT_REVIEW.md`: Core's generic full-runtime snapshot remains intentionally bounded, while RTSBenchmark demonstrates deterministic tick-boundary resume by persisting app-owned truth and reconstructing runtime objects.

## M9: Release/NativeAOT reporting and public benchmark claims

M9 polishes `Dominatus.RTSBenchmark` as a quotable, shareable CPU benchmark report format. The benchmark remains a pure behavioral-AI workload: ships run deterministic utility decisions, tactical sensing, event exchange, action emission/resolution, phase timing, diagnostics, and determinism hashing. The measured loop does **not** include rendering, GPU work, windowing, network calls, or live model/LLM inference.

### Build configuration guidance

Use the build mode as part of every benchmark claim:

- **Debug `dotnet run`** is useful for development and diagnostics only. Do not use Debug results for public performance claims.
- **Release `dotnet run -c Release`** is the recommended local baseline for ad-hoc benchmark results.
- **NativeAOT published executable** is preferred for public benchmark claims when the target platform can publish the sample successfully.

Release single-run example with machine-readable exports:

```bash
dotnet run -c Release --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- \
  --mode Skirmish \
  --no-checkpoints \
  --json artifacts/rts-skirmish.json \
  --csv artifacts/rts-skirmish.csv
```

Sequential comparison example for public comparison material:

```bash
dotnet run -c Release --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- \
  --compare-sensor-cadence \
  --mode Skirmish \
  --trials 5 \
  --no-broadscan-baseline \
  --trial-details \
  --json artifacts/rts-skirmish-compare.json \
  --csv artifacts/rts-skirmish-compare.csv
```

### NativeAOT publish guidance

Do not enable `PublishAot` globally in the project file because normal build/test workflows should stay fast and portable. Instead publish explicitly for the current platform and runtime identifier.

Linux x64 example:

```bash
dotnet publish samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj \
  -c Release \
  -r linux-x64 \
  -p:PublishAot=true \
  --self-contained true \
  -o artifacts/rtsbenchmark-native

./artifacts/rtsbenchmark-native/Dominatus.RTSBenchmark \
  --mode Skirmish \
  --no-checkpoints \
  --json artifacts/rts-skirmish-native.json \
  --csv artifacts/rts-skirmish-native.csv
```

Windows x64 example:

```powershell
dotnet publish samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj `
  -c Release `
  -r win-x64 `
  -p:PublishAot=true `
  --self-contained true `
  -o artifacts/rtsbenchmark-native

.\artifacts\rtsbenchmark-native\Dominatus.RTSBenchmark.exe `
  --mode Skirmish `
  --no-checkpoints `
  --json artifacts/rts-skirmish-native.json `
  --csv artifacts/rts-skirmish-native.csv
```

NativeAOT support can vary by SDK, OS, architecture, and installed native toolchain. The report includes an `isNativeAot` indicator inferred from `RuntimeFeature.IsDynamicCodeSupported`; treat this as a practical indicator, not a formal proof of how the executable was produced.

### JSON and CSV export

`--json PATH` writes the full report object using `System.Text.Json` with camel-case property names and string enum values.

- Single-run JSON exports `RtsBenchmarkResult`.
- Comparison JSON exports `RtsBenchmarkComparisonResult`.
- Reports include benchmark options, scores/rates, phase timings, diagnostics, determinism hashes, and environment metadata.
- Reports do not include raw per-ship state.

`--csv PATH` writes a compact summary for spreadsheet and README use.

- Single-run CSV writes one row with mode, sensor mode, dynamic cadence state, score/rates, elapsed/measured milliseconds, selected phase timings, allocation summary, and determinism hash.
- Comparison CSV writes one row per configuration summary with mean/median rates, phase timings, allocation summary, skip-rate summary, and hash stability.

The CLI creates parent directories for export paths when possible. The special path `-` is intentionally not supported for exports; use a file path so reports can be attached and cited directly.

### Long-run progress output

Comparison mode defaults to trial progress output so long Battle runs are not silent:

```text
[comparison] starting SpatialGrid + cadence trial 1/5
[comparison] completed SpatialGrid + cadence trial 1/5: 123456.78 agent-ticks/sec, hash ...
```

Use `--progress-interval-seconds N` to request comparison trial start/completion progress explicitly, or `--progress-interval-seconds 0` to suppress the default comparison progress unless `--trial-details` is also used. For single long runs, keep checkpoint output enabled or choose an explicit checkpoint cadence, for example `--checkpoint-interval 250`.

### Environment metadata in reports

Every single-run and comparison result includes:

- OS description;
- process architecture;
- framework description;
- runtime identifier;
- processor count;
- NativeAOT indicator;
- configuration hint.

These fields are intended to make reports self-describing enough to quote without scraping console text.

### Battle-friendly comparison workflow

For public comparisons, prefer repeated sequential trials:

```bash
dotnet run -c Release --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- \
  --compare-sensor-cadence \
  --mode Battle \
  --trials 5 \
  --no-broadscan-baseline \
  --progress-interval-seconds 10 \
  --json artifacts/rts-battle-compare.json \
  --csv artifacts/rts-battle-compare.csv
```

Avoid mixing sequential single-thread scores with `--parallel-trials` throughput results. Parallel comparison mode is useful for stress testing independent benchmark instances, but CPU contention changes what the numbers mean.

### Benchmark claim template

Use a concise claim that includes mode, build, hardware/OS, trial count, rates, sensor settings, hash stability, and exclusions:

```text
Dominatus.RTSBenchmark Skirmish, Release/NativeAOT, {CPU/OS}, {date}:
{median agent-ticks/sec} median over {n} sequential trials,
{median decisions/sec} decisions/sec,
{sensor mode}, dynamic cadence {enabled/disabled},
stable hash {yes/no},
no rendering/GPU/network/model inference in measured loop.
Command: {exact command line}
Report: {JSON/CSV artifact path or URL}
```

### Avoid misleading claims

- Do not compare Debug runs to Release or NativeAOT runs.
- Do not compare parallel-trial throughput to sequential single-thread scores without labeling it clearly.
- Always document hardware, OS, SDK/runtime, command line, mode, sensor mode, cadence mode, and trial count.
- Use repeated sequential trials for public comparisons.
- Keep the JSON/CSV artifacts with the public claim so readers can inspect phase timings, diagnostics, environment metadata, and determinism hashes.

## M10 benchmark-local parallel decision phase

M10 adds an optional, benchmark-local parallel fast path for the RTSBenchmark **decision phase only**. It deliberately does not add a generic Core `ParallelAiWorldRunner`, staged Core context injection, parallel sensors, parallel mailbox delivery, parallel action resolution, or any Core semantic changes.

The safe subset is narrow:

- one `AiAgent` owns one ship;
- the sensor phase has already mirrored each ship's tactical summary into that agent's local blackboard;
- RTS utility scorers read agent-local blackboard values;
- decision work does not write `WorldBb`, send mailbox messages, or dispatch actuations;
- each worker ticks one ship/agent and stages one local action result;
- actions are merged back in deterministic ship-id order;
- the existing action sort and action resolution remain single-threaded and deterministic.

Enable it for a single run with:

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- \
  --mode Skirmish \
  --parallel-agents \
  --max-degree 8 \
  --no-checkpoints
```

`--max-degree N` bounds the decision-phase worker degree. If `--parallel-agents` is supplied without `--max-degree`, the benchmark uses `Environment.ProcessorCount`. A supplied `--max-degree` must be at least one.

### `--parallel-agents` vs `--parallel-trials`

These flags measure different things:

- `--parallel-agents` runs one benchmark trial whose ship-agent decision phase is parallelized internally.
- `--parallel-trials` runs multiple independent comparison trials concurrently.

They can both increase CPU use, but they answer different questions. Do not report `--parallel-trials` throughput as a single-trial decision-phase speedup.

### Comparing sequential and parallel agents

M10 includes an agent-parallelism comparison command:

```bash
dotnet run --project samples/Dominatus.RTSBenchmark/Dominatus.RTSBenchmark.csproj --framework net10.0 -- \
  --compare-agent-parallelism \
  --mode Skirmish \
  --trials 3 \
  --max-degree 8
```

The comparison runs sequential agents and parallel decision agents with the same mode and sensor/cadence settings. It reports median agent-ticks/sec for both configurations, a speedup ratio, per-configuration hash stability, and whether the sequential and parallel deterministic hashes match.

### Determinism contract

Sequential and parallel decision runs should produce the same deterministic hash and deterministic counters for the same options except `ParallelAgents`. Timing, allocations, elapsed milliseconds, and parallel diagnostics may differ and should not be used for equivalence.

The following phases remain single-threaded in M10:

- cooldown;
- sensor and sensor cadence;
- mailbox/event delivery;
- action sort;
- action resolution;
- checkpoint/save/load;
- hashing;
- final metrics/reporting.

Single-run JSON and CSV exports include parallel-agent metadata such as `parallelAgents`, `maxDegreeOfParallelism`, and `executionMode`. Console reports include parallel decision counters for agent ticks, tasks scheduled, faults, and staged local actions.

### Why this is benchmark-local

The RTSBenchmark can take this fast path because its current decision nodes and scorers already fit the safe subset. Core still exposes direct `ctx.WorldBb`, `ctx.Mail`, and `ctx.Act` surfaces, so a general Core parallel runner needs staged/facade context surfaces before it can be safe for arbitrary authored graphs.

Future work remains:

- Core staged `ParallelAiWorldRunner`;
- staged `WorldBb`, mailbox, and actuation surfaces;
- parallel sensor phase over immutable snapshots;
- deterministic parallel action-resolution partitions.
