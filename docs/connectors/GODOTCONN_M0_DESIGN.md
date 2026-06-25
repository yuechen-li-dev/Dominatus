# Dominatus.GodotConn M0 Design

## Purpose

`Dominatus.GodotConn` is the Godot-native adoption bridge for Dominatus.

Its job is to let a Godot developer attach a Dominatus HFSM to a scene tree the same way they would attach any other `Node`, while keeping Dominatus concepts visible:

- HFSM stays an HFSM.
- blackboard stays a blackboard.
- mailbox stays a mailbox.
- actuators stay typed actuator commands and handlers.

M0 is intentionally small. It proves the public API shape, lifecycle, and packaging path without taking on editor tooling or a visual authoring surface.

## Doctrine

The design doctrine for this connector is:

> GodotConn adapts Dominatus to Godot lifecycle. It does not disguise Dominatus as a Godot-only behavior tree.

That means:

- Godot owns scene tree lifetime, node attachment, `_Ready`, `_ExitTree`, `_Process`, `_PhysicsProcess`, and engine-facing APIs.
- Dominatus owns behavior execution, HFSM state, blackboard facts, mailbox delivery, and typed actuation.
- The connector should feel like a natural Godot node API, but it should not rename away Dominatus concepts.

## Success and failure modes

Success in M0 looks like this:

1. A Godot C# project references the package.
1. The project adds one `DominatusWorldNode` scene node or autoload.
1. A gameplay node attaches a subclass of `DominatusAgentNode`.
1. The subclass returns an `HfsmGraph`, registers any actuator handlers it needs, and optionally seeds its blackboard.
1. The scene runs without the game manually ticking `AiWorld`.

Failure in M0 would look like this:

- hidden global world creation
- agents ticking themselves outside the world
- Godot node lifetime drifting out of sync with `AiWorld`
- a wrapper that obscures HFSM/blackboard/mailbox concepts until something goes wrong

## Public API

M0 adds:

- `DominatusTickMode`
- `DominatusWorldNode`
- `DominatusAgentNode`
- `GodotBbKeys`
- `BlackboardGodotExtensions`
- `Move2DCommand` + `Move2DActuationHandler`
- `PlayAnimationCommand` + `PlayAnimationActuationHandler`

### `DominatusTickMode`

```csharp
public enum DominatusTickMode
{
    Process,
    PhysicsProcess,
    Manual
}
```

Default is `PhysicsProcess`.

Reasoning: gameplay and character behavior in Godot usually wants physics cadence by default, and the included 2D movement actuator is safest there.

### `DominatusWorldNode`

`DominatusWorldNode : Node` owns:

- one `AiWorld`
- one shared `ActuatorHost`
- registration of attached `DominatusAgentNode` instances
- automatic ticking from `_Process` or `_PhysicsProcess`

Surface:

```csharp
public partial class DominatusWorldNode : Node
{
    public AiWorld World { get; }
    public ActuatorHost Actuators { get; }
    public DominatusTickMode TickMode { get; set; }
    public double LastDeltaSeconds { get; }
    public ulong TicksProcessed { get; }

    public void RegisterAgent(DominatusAgentNode agent);
    public void UnregisterAgent(DominatusAgentNode agent);
    public void Tick(double deltaSeconds);
}
```

### `DominatusAgentNode`

`DominatusAgentNode : Node` is the Godot-facing brain node you attach to a character, NPC root, or other gameplay object.

Surface:

```csharp
public abstract partial class DominatusAgentNode : Node
{
    public NodePath WorldPath { get; set; }
    public int Team { get; set; }
    public bool IsAlive { get; set; }

    public DominatusWorldNode WorldNode { get; }
    public AiWorld World { get; }
    public AiAgent? Agent { get; }
    public Blackboard Bb { get; }
    public ActuatorHost Actuators { get; }
    public AgentId AgentId { get; }
    public string CurrentLeafState { get; }

    public void AttachToWorld(DominatusWorldNode world);
    public void SendMessage<T>(T message) where T : notnull;
    public bool SendMessageTo<T>(AgentId recipient, T message) where T : notnull;

    protected abstract HfsmGraph ConfigureGraph();
    protected virtual HfsmOptions CreateHfsmOptions();
    protected virtual void ConfigureActuators(ActuatorHost actuators);
    protected virtual void ConfigureBlackboard(Blackboard blackboard);
    protected virtual Node GetSpatialNode();
}
```

Important note: the design note suggested `ConfigureGraph(HfsmGraph graph)`, but current `Dominatus.Core` authoring requires `HfsmGraph.Root` to be provided at construction time. M0 therefore uses `ConfigureGraph()` returning a fully constructed `HfsmGraph` so the connector stays compatible with the real `Dominatus.Core` API.

## Lifecycle

M0 lifecycle behavior is:

- `DominatusAgentNode._Ready()`
  - resolves a world
  - builds the graph
  - creates `AiAgent`
  - runs actuator and blackboard setup hooks
  - registers with `DominatusWorldNode`
- `DominatusWorldNode.RegisterAgent(...)`
  - adds the agent to `AiWorld`
  - seeds its public snapshot
- `DominatusWorldNode._Process(...)` or `_PhysicsProcess(...)`
  - updates public snapshots
  - calls `AiWorld.Tick(dt)`
  - emits state-change signals if leaf state changed
- `DominatusAgentNode._ExitTree()`
  - unregisters from the world
- `DominatusWorldNode.UnregisterAgent(...)`
  - removes the `AiAgent` from `AiWorld`
  - drops its public snapshot

This required one small core seam: `AiWorld` now supports removing agents cleanly.

## World ownership and discovery

World discovery is explicit and ordered:

1. If `WorldPath` is set, use it.
1. Otherwise search ancestors for a `DominatusWorldNode`.
1. Otherwise look for an autoload at `/root/DominatusWorld`.
1. Otherwise fail with a clear error.

M0 does not silently create hidden global worlds.

That keeps scene behavior understandable and makes it obvious whether a behavior tree is scene-local or using a project-global autoload.

## Tick modes

### `PhysicsProcess`

- default
- best fit for character gameplay
- recommended when using `Move2DActuationHandler`

### `Process`

- useful for non-physics behavior or UI-ish behavior surfaces
- still supported by the same world node

### `Manual`

- world does not auto-tick
- caller must invoke `DominatusWorldNode.Tick(deltaSeconds)` explicitly
- useful for tests, deterministic harnesses, or unusual simulation ownership

Agents do not tick themselves in any mode.

## Signal and mailbox bridge

M0 intentionally ships a small bridge, not a reflection-heavy generic signal layer.

Implemented:

- `SendMessage<T>(T message)` publishes into the local agent event bus
- `SendMessageTo<T>(AgentId recipient, T message)` routes through `AiWorld.Mail`
- `DominatusMessageSent` signal emits the CLR message type name on local sends
- `DominatusStateChanged` emits when the active leaf state changes after a world tick

Deferred for later:

- generic mailbox-to-signal projection
- blackboard change signals
- inspector-driven signal routing

## Blackboard guidance

`GodotBbKeys` provides a tiny convenience layer for common Godot value types:

- `GodotBbKeys.Vector2(...)`
- `GodotBbKeys.Vector3(...)`
- `GodotBbKeys.NodePath(...)`

`BlackboardGodotExtensions.TryResolveNode(...)` resolves a stored `NodePath` back to a typed node relative to a supplied owner.

Doctrine:

- `NodePath` is the default durable reference shape.
- live `Node` references may be convenient at runtime, but they should be treated as runtime-only and not persistence-safe.
- do not treat live Godot object references as replay-safe or save-safe state.

## Actuator pattern

M0 keeps the actuator story deliberately light.

### Included handlers

- `Move2DCommand` + `Move2DActuationHandler`
- `PlayAnimationCommand` + `PlayAnimationActuationHandler`

### Movement semantics

`Move2DActuationHandler` works in two modes:

- for `CharacterBody2D`, it sets `Velocity` and optionally calls `MoveAndSlide()`
- for plain `Node2D`, it integrates `Position += velocity * LastDeltaSeconds`

This is enough to prove the API shape without committing M0 to a larger movement framework.

### Animation semantics

`PlayAnimationActuationHandler` simply calls `AnimationPlayer.Play(name)`.

## Sample plan

M0 does not add a full Godot sample project yet.

Instead, M0 ships a complete quickstart doc with runnable code snippets for:

- adding a world node or autoload
- attaching a `GuardBrain : DominatusAgentNode`
- defining a one-state patrol loop
- registering 2D movement and animation handlers

An M1 sample can add a small `Guard Patrol 2D` Godot project once package and sample layout policy is settled.

## Testing limitations

M0 does not add a separate `Dominatus.GodotConn.Tests` project.

Reason:

- the connector public surface is built on `Godot.Node` and related engine types
- headless xUnit coverage without the Godot runtime/editor would be brittle and low-confidence
- M0 prefers direct compile/build validation over fake tests that exercise the wrong environment

Validation for M0 is therefore:

- direct project build
- full solution build/test sanity where feasible
- package pack validation

## Packaging and distribution notes

### What was verified

- Official Godot 4 C# docs state that Godot uses .NET 8.0.
- Official docs also describe Godot's C# support as NuGet-package based, with `Godot.NET.Sdk` and managed packages such as `GodotSharp`.
- This connector builds as a normal `net8.0` library with a compile-time `GodotSharp` reference.

### M0 recommendation

Ship `Dominatus.GodotConn` as a normal NuGet package first.

Why:

- it matches Godot's C# package story
- it keeps the connector small and CI-friendly
- it lets the Godot project stay the place where `Godot.NET.Sdk` is configured

### Not shipped in M0

- no `addons/` folder distribution
- no editor plugin wrapper
- no custom inspector tooling

An addon wrapper can still be added later if the project wants a drag-and-drop installation path, but M0 should stay NuGet-first.

### Versioning note

The connector is currently aligned to the Godot 4.7 managed package family (`GodotSharp` `4.7.0`, sample project using `Godot.NET.Sdk` `4.7.0`). Consuming Godot projects should keep their Godot-managed package versions aligned with their engine version. If a project standardizes on a different Godot 4.x package family, the connector package version may need to track that baseline in a later milestone.

## Non-goals

M0 does not include:

- visual graphs
- behavior tree skinning
- editor tooling
- ECS integration
- navigation framework
- persistence/replay adapters
- multiplayer or lockstep
- large sample content
- source generators

## M1 roadmap

Good M1 candidates:

1. small Godot sample project proving scene-tree lifecycle in a real Godot 4.7 .NET scene
1. richer state inspection and diagnostics hooks
1. optional blackboard-change signaling
1. small scene setup helper or installer node
1. version-policy hardening for broader Godot 4.x consumption
