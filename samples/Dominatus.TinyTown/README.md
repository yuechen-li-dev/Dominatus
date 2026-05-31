# TinyTown — A Life Simulation Sample for Dominatus

> **LLMs are actors. Utility AI is the director.**

TinyTown is a working town simulation built on [Dominatus](https://github.com/yuechen-li-dev/Dominatus), a deterministic .NET 8 behavioral AI kernel. It demonstrates something that the broader AI agent industry has not yet figured out: **LLMs do not need to be in the control loop to produce believable, coherent, emotionally resonant AI behavior.** They need to be in the right part of the loop.

This distinction sounds subtle. It is not. It is the entire ballgame.

---

## What TinyTown Actually Does

Four NPCs — Maya, Theo, Lina, and Nia — live, work, eat, sleep, socialize, and maintain relationships across a simulated town. Their behavior is autonomous. Their dialogue is LLM-generated. Their emotional state evolves based on what happens between them. Memories of past interactions influence future conversations.

It runs in 742 lines of C#. It has a full test suite. It uses zero live LLM API calls for ordinary behavior.

---

## The Problem with Every Other Approach

Since Stanford's Generative Agents paper (2023), the dominant approach to believable NPC behavior has been: ask an LLM what the character wants to do, let the LLM decide what they say, let the LLM update their memory, let the LLM reflect on their day, let the LLM plan tomorrow.

This approach has three fatal problems in production:

**It is expensive.** Every decision — should Maya eat, sleep, or go to work? — costs an LLM call. With hundreds of NPCs running in a game world, this is economically impossible. Even with a single agent it produces latency that breaks any real-time simulation.

**It is nondeterministic.** The same inputs produce different outputs. You cannot test it, replay it, audit it, or guarantee it behaves correctly under edge cases. When an NPC does something wrong, you cannot reproduce the failure.

**It makes the LLM responsible for things LLMs are bad at.** Whether Maya is hungry is a float on a blackboard. Whether she should eat right now is a utility score comparison. These are not semantic problems. Routing them through a language model to get back "Maya should eat because her hunger is 0.08" burns tokens to do arithmetic.

The result: demo-quality AI that breaks under production constraints, costs a fortune to run, and cannot be audited or debugged.

---

## What TinyTown Does Instead

TinyTown draws a precise line between what is a **simulation problem** and what is a **semantic problem**.

### Simulation problems — zero LLM calls

Every tick, needs decay by a fixed rate:

```csharp
Dec(agent, HungerKey,  0.015f);
Dec(agent, EnergyKey,  0.012f);
Dec(agent, SocialKey,  0.012f);
Dec(agent, BladderKey, 0.025f);
// ...
```

Every tick, each townie runs `Ai.Decide` — Dominatus's utility-scored autonomous decision step:

```csharp
yield return Ai.Decide(new DecisionSlot("TinyTown.maya.NextAction"),
[
    Ai.Option("UseBathroom", NeedUrgency(BladderKey)),
    Ai.Option("Eat",         NeedUrgency(HungerKey)),
    Ai.Option("Sleep",       NeedUrgency(EnergyKey)),
    Ai.Option("GoToWork",    new Consideration((w, a) => ScoreWork(w, a, profile))),
    Ai.Option("Chat",        new Consideration((w, a) => ScoreChat(w, a, profile))),
    Ai.Option("VisitFriend", new Consideration((w, a) => ScoreVisitFriend(a, profile))),
    // ...
], hysteresis: 0.02f);
```

This is not prompting. This is not a chain. This is a typed utility function running in a deterministic runtime at tick speed. Maya eats when she is hungry. She goes to work during her shift. Lina, an introvert, visits friends at the Park rather than the Cafe — her `ScoreVisitFriend` multiplies the base score by `0.55f`. Theo, a HardWorker, gets a 0.92 score for `GoToWork` vs 0.82 for others. These are not personality descriptions handed to an LLM. They are typed considerations in a mathematical scoring system.

Zero LLM calls. Zero tokens. Zero cost. Deterministic, replayable, testable.

### Semantic problems — one precisely scoped LLM call

When two friends end up in the same location and utility scoring selects `Chat`, the situation becomes genuinely semantic. What do they say? Given their history — given that Theo missed Maya's work celebration, that their relationship carries real tension alongside real affection — what happens between them?

This is what language models are actually good at. TinyTown calls the LLM exactly here, with full context:

```
- Speaker: Maya (Engineer, Social, HardWorker)
- Listener: Theo (Barista, Playful, Social)
- Location: Cafe
- Maya's needs: hunger=0.72, energy=0.78, social=0.10 (she needs this)
- Theo's needs: hunger=0.80, energy=0.82, social=0.95 (he doesn't)
- Relationship: affinity=0.65, tension=0.45
- Unresolved issue: missed-celebration
- Relevant memory: "Theo missed Maya's work celebration after promising to come.
  Maya felt ignored, and Theo avoided the subject afterward."

Persona: Bounded life-sim DM. Narrate the scene and propose social 
consequences only; the engine commits state.
```

The LLM returns:

```json
{
  "dialogue": "Maya: So... are we pretending you didn't miss my celebration?\nTheo: I wasn't pretending. I just didn't know how to apologize.",
  "tone": "awkward",
  "outcome": "partial_repair",
  "affinityDelta": 0.08,
  "tensionDelta": -0.12,
  "memorySummary": "Maya and Theo had an awkward but honest conversation about Theo missing her work celebration. The tension eased slightly, but the issue is not fully resolved."
}
```

**The engine then validates, clamps, and commits.** The LLM cannot exceed ±0.25 per scene on any relationship delta. It cannot write dialogue longer than 2,000 characters. It cannot modify state it was not asked about. NaN, Infinity, and out-of-range values are treated as 0 or clamped before commit. The engine runs the world. The LLM narrates moments in it.

This is not a prompt engineering trick. It is an architectural boundary enforced in code.

---

## The Relationship and Memory System

Relationships are not flavor. They are typed state:

```csharp
public sealed record RelationshipState
{
    public required string A { get; init; }
    public required string B { get; init; }
    public float Affinity { get; init; }           // 0..1
    public float Tension { get; init; }            // 0..1
    public int LastInteractionTick { get; init; }
    public string? UnresolvedIssueId { get; init; }
}
```

Relationships use deterministic, order-independent keys (`maya:theo`). They are seeded with real history. Maya and Theo begin with `Affinity: 0.65`, `Tension: 0.45`, and `UnresolvedIssueId: "missed-celebration"`. There is a memory record explaining what happened.

Every chat appends a new `TownMemoryRecord`. Memories are filtered by participant and passed as context to subsequent conversations between the same pair. Without vector retrieval, without embedding search, without a reflection loop — just typed records filtered by ID.

The result: dialogue that references shared history. NPCs that remember. Relationships that evolve. All grounded in typed state that the runtime owns and the LLM can only propose changes to.

---

## Typed Inter-Agent Coordination

Social coordination does not happen through shared mutable state. When a townie chooses `VisitFriend`, they send a typed mailbox message:

```csharp
world.Mail.Send(to, new FriendVisitRequested(
    FromTownieId: "maya",
    ToTownieId:   "lina",
    Location:     "Park",
    Tick:         42));
```

The recipient agent consumes this event on a future tick, moves to the proposed location, and increments their own social need. Location convergence for social interaction emerges from typed message passing, not from a shared mutable location dictionary. This is auditable. It is reproducible. It appears in the event log with both the send and the receive.

---

## The Test Suite Tells the Story

The companion test project covers 14 scenarios including:

- **Hungry scenario**: Maya's hunger is 0.05, all other needs full. Verifies `Eat` is selected. Zero LLM calls.
- **Tired scenario**: Energy is 0.05. Verifies `Sleep`. Zero LLM calls.
- **Work scenario**: Tick is within shift hours. Verifies `GoToWork`. Zero LLM calls.
- **Awkward Maya/Theo conversation**: Dialogue is generated. Relationship deltas are applied. Memory is appended.
- **Friendly Theo/Nia conversation**: Different pair, different tone, different outcome.
- **Invalid delta clamp scenario**: LLM returns `affinityDelta: 999, tensionDelta: -999`. Engine clamps to ±0.25. Relationship does not explode.
- **Determinism**: Same seed, same scenario, same outcome. Always.

The invalid delta clamp test is particularly instructive. It is a security test. It verifies that a hallucinating or adversarial LLM cannot corrupt the simulation state beyond the bounds the engine defines. The engine is not trusting the LLM. It is validating the LLM's output the same way it would validate any external input.

---

## What This Implies

TinyTown is a sample project. It has four townies and three locations. It is not a shipped game.

What it demonstrates is a complete architecture for NPC AI that scales:

**Token cost scales with social interactions, not with the number of agents.** A world with 1,000 NPCs does not require 1,000 LLM calls per tick. It requires zero LLM calls per tick for ordinary behavior and one call per actual conversation that occurs. Most ticks, most agents eat, sleep, work, and move. Tokens are spent on the moments that matter.

**Determinism is built in from first principles.** The HFSM, the blackboard, the utility scoring, the relationship state, the memory records — all of it is deterministic typed state. LLM calls are cassette-replayable. The entire simulation can be rewound to any tick, a decision overridden, and the consequences played forward. No other agent framework on any platform has this property.

**Adding voice is one afternoon of work.** The LLM already produces the dialogue line. A `SpeakLineCommand` actuator wrapping ElevenLabs or Kokoro routes through Dominatus's standard actuation pipeline — allowlisted voices, timeout, byte-capped audio. Lip sync is an engine-side concern. The behavior system is already complete.

**The architecture is the same for games and production agents.** The same runtime that runs TinyTown's Maya also runs home automation agents, Microsoft Graph email assistants, and multi-LLM consensus decisions behind a human approval gate. The safety primitives — allowlists, actuation policies, approval gates, bounded output validation — are not game-specific. They apply whenever an AI system takes actions with consequences in the world.

---

## Running TinyTown

```bash
dotnet run --project samples/Dominatus.TinyTown/Dominatus.TinyTown.csproj
```

Output includes tick count, final townie snapshots, event log, dialogue lines generated, LLM call count, and relationship state. A 100-tick run produces one LLM call per conversation that occurs. All other behavior is free.

---

## The Broader Context

The game AI industry runs on behavior trees — a technology from the early 2000s that polls stateless node graphs every frame, handles async behavior through decorator workarounds, and requires external state management because the tree itself owns nothing.

The AI agent industry runs on LLM-centric frameworks where the model is the control flow, routing costs tokens, and "safety" means a system prompt.

TinyTown is a working demonstration that neither of these is necessary. A typed coroutine runtime with utility scoring and a precisely scoped LLM integration produces NPC behavior that is more coherent, cheaper to run, easier to debug, and more auditable than either approach — in 742 lines of standard C# that any .NET developer can read, modify, and extend without learning a new language, a new visual scripting system, or a new framework.

The code is not impressive because it is complex. It is impressive because it is not.

---

*TinyTown is a sample in the [Dominatus](https://github.com/yuechen-li-dev/Dominatus) repository. Dominatus is a deterministic .NET 8 behavioral AI kernel for stateful agents, available on NuGet.*
