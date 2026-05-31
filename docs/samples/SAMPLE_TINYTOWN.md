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

## M1: LLM-as-DM relationship scenes

TinyTown now treats dialogue as a bounded DM-style scene adjudication step rather than flavor text alone. Utility AI still decides when two simulated townies should talk; the LLM is only asked to perform the semantic scene and propose consequences.

> The DM may narrate consequences. The engine commits state.

The runtime owns durable state. The fake LLM DM may write dialogue, interpret tone and outcome, propose bounded relationship deltas, and propose a memory summary. Dominatus then validates, clamps, and commits the state changes.

### Relationship state

Friend pairs have deterministic, order-independent relationship keys such as `maya:theo`. Each relationship snapshot includes:

- `A` / `B`
- `Affinity` in `0..1`
- `Tension` in `0..1`
- `LastInteractionTick`
- optional `UnresolvedIssueId`

The seeded relationships include Maya/Theo with moderate affinity, notable tension, and the unresolved `missed-celebration` issue; Maya/Lina and Theo/Nia start warmer and less tense.

### Social memory continuity

TinyTown has explicit `TownMemoryRecord` entries for structured continuity. The seed memory for Maya/Theo says Theo missed Maya's work celebration after promising to come. Scene context filters memory by the two chat participants; there is no vector retrieval, no embedding search, and no reflection loop.

### Structured scene outcome

`Chat` expects the LLM DM to return JSON matching `DialogueSceneOutcome`:

```csharp
public sealed record DialogueSceneOutcome
{
    public required string Dialogue { get; init; }
    public required string Tone { get; init; }
    public required string Outcome { get; init; }
    public float AffinityDelta { get; init; }
    public float TensionDelta { get; init; }
    public required string MemorySummary { get; init; }
}
```

The runtime validates required text fields, bounds text lengths, treats invalid numeric deltas as `0`, clamps per-scene affinity and tension deltas to `-0.25..0.25`, and clamps final relationship values to `0..1`. The LLM cannot change unrelated state.

After a valid chat, the runtime:

1. applies clamped deltas to the pair relationship,
2. updates `LastInteractionTick`,
3. appends a deterministic dialogue memory such as `memory.maya-theo.chat.0`,
4. records the dialogue line, and
5. logs the DM outcome, relationship delta, and memory commit.

### Fake deterministic DM

There are still no live LLM providers, network calls, API keys, OpenRouter, Semantic Kernel, or MCP. The sample uses a scripted fake `ILlmClient`. Maya/Theo returns an awkward `partial_repair` scene for the missed celebration; generic friend chats return a warm `friendly_chat` scene. Invalid-delta scenarios can force huge fake deltas to prove runtime clamping.

## LLM role: dialogue only

TinyTown uses `Llm.Call` only when `Chat` is selected. The call has a stable id such as:

```text
tinytown.dialogue.maya.theo.1
```

The context includes speaker/listener names, location, profile and trait summaries, current needs, relationship affinity/tension/unresolved issue, relevant prior memories, and the instruction to return structured scene outcome JSON. A deterministic fake `ILlmClient` returns scripted JSON that includes dialogue such as:

```text
Maya: So... are we pretending you didn’t miss my celebration?
Theo: I wasn’t pretending. I just didn’t know how to apologize.
```

There are no live LLM providers, no network calls, no API keys, and no model calls for ordinary actions such as eating, sleeping, working, bathroom use, showering, having fun, visiting, or idling. Non-chat actions do not mutate relationships through the LLM.

## Not a Stanford-style reflection loop

TinyTown intentionally does not implement a natural-language memory stream as scheduler, vector retrieval, embedding search, or reflection loop. It is a runtime-first life simulation: durable state and utility scoring drive behavior, while LLM text generation is reserved for bounded semantic scenes.

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

It covers completion, hungry/tired/work/social scenarios, dialogue-only LLM usage, structured dialogue outcomes, relationship snapshots and deltas, memory append behavior, prompt context memory, invalid-delta clamping, non-dialogue no-LLM behavior, mailbox event sequencing, determinism, and the absence of live-provider dependencies.

## Future work

Possible future extensions:

- many more agents
- a parallel tick scheduler
- richer relationship and social availability models
- persistence/replay snapshots
- UI visualization
