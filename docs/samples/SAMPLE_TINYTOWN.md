# TinyTown sample

`Dominatus.TinyTown` is a small deterministic town simulation that demonstrates a runtime-first alternative to LLM-first town simulations.

The sample thesis is:

> LLMs are actors. Utility AI is the director.

Dominatus does not ask an LLM whether a character is hungry. Hunger is state. Eating is utility. Conversation is where the LLM belongs.

## Purpose

TinyTown models four townies moving through ordinary life-simulation ticks:

- Maya, an engineer with `HardWorker` and `Social` traits.
- Theo, a barista with `Playful` and `Social` traits.
- Lina, an artist with `Creative` and `Introvert` traits.
- Nia, a clerk with `Neat` and `Serious` traits.

Locations are intentionally minimal: `Home`, per-townie homes, `Work`, `Cafe`, and `Park`. The point is not to build a full Sims clone; the point is to show the runtime boundary between deterministic simulation and semantic flavor.

## Utility-first life simulation

Each townie has stable identity and invariants represented as C# records:

- `TownieProfile`
- `TownieTraits`
- `WorkSchedule`

Mutable state lives in the agent blackboard:

- current location
- current action
- hunger
- energy
- social
- fun
- hygiene
- bladder

Needs are floats in the range `0..1`, where `1.0` means satisfied and `0.0` means urgent. The sample decays needs every tick and clamps them back into range after decay or action effects.

## `Ai.Decide` action selection

Each townie owns an HFSM with a persistent root decision node. The decision slot is named:

```text
TinyTown.{townieId}.NextAction
```

The root emits `Ai.Decide` over these actions:

- `UseBathroom`
- `Eat`
- `Sleep`
- `Shower`
- `GoToWork`
- `HaveFun`
- `VisitFriend`
- `Chat`
- `Idle`

Need-driven actions score as urgency (`1 - need`). Work scores high inside the townie's schedule, with a bonus for `HardWorker`. Social actions score from social need plus trait modifiers. `Chat` requires a co-located friend.

The HFSM then executes the selected state, and the sample runner applies action effects to blackboard state.

## Mailbox/social coordination

Social behavior is not direct mutation soup. When a townie chooses `VisitFriend`, the sample sends a typed mailbox event:

```csharp
public sealed record FriendVisitRequested(
    string FromTownieId,
    string ToTownieId,
    string Location,
    int Tick);
```

The recipient consumes the request on a later tick and moves to the proposed social location. Event-log entries show both the request and later receipt, making the coordination path visible and testable.

## LLM role: dialogue only

TinyTown uses `Llm.Call` only when `Chat` is selected. The call has a stable id such as:

```text
tinytown.dialogue.maya.theo.1
```

The context includes speaker, listener, location, need summary, and relationship. A deterministic fake `ILlmClient` returns scripted flavor text such as:

```text
Maya: Good to see you, Theo. Work has been a lot today.
```

There are no live LLM providers, no network calls, no API keys, and no model calls for ordinary actions such as eating, sleeping, working, bathroom use, showering, having fun, visiting, or idling.

## Not a Stanford-style reflection loop

TinyTown intentionally does not implement a natural-language memory stream, vector retrieval, embedding search, or reflection loop. It is a runtime-first life simulation: durable state and utility scoring drive behavior, while LLM text generation is reserved for bounded semantic presentation.

This sample is therefore a contrast case: believable town agents do not require the runtime to ask a model what every character wants on every tick.

## Running

```bash
dotnet run --project samples/Dominatus.TinyTown/Dominatus.TinyTown.csproj --framework net10.0
```

The console output prints:

- ticks run
- final townie summaries
- event highlights
- dialogue lines
- LLM call count
- a note that normal utility actions do not call LLMs

## Tests

The companion test project is:

```text
tests/Dominatus.TinyTown.Tests
```

It covers completion, hungry/tired/work/social scenarios, dialogue-only LLM usage, non-dialogue no-LLM behavior, mailbox event sequencing, determinism, and the absence of live-provider dependencies.

## Future work

Possible future extensions:

- many more agents
- a parallel tick scheduler
- richer relationship and social availability models
- persistence/replay snapshots
- UI visualization
