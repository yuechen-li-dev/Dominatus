# Dominatus.GodotConn M0 Quickstart

This quickstart shows the smallest useful Godot 4 C# setup for Dominatus:

1. reference `Dominatus.GodotConn`
1. add one `DominatusWorldNode`
1. attach a `DominatusAgentNode` subclass to a character
1. return an `HfsmGraph`
1. register a simple movement actuator

For a complete runnable sample, see [GodotConn M1 TinyTown sample](GODOTCONN_M1_TINYTOWN_SAMPLE.md).

## Install

In a Godot 4 C# project, add a package reference to `Dominatus.GodotConn`.

The Godot project itself should continue to use Godot's normal C# project setup. M0 assumes a standard Godot 4 .NET project, not a custom runtime host.

## Add a world node

Choose one:

- scene-local: add `DominatusWorldNode` to your scene tree
- autoload: add a node named `DominatusWorld` to project autoloads

Scene-local is the easiest way to start.

## Create a guard brain

Attach a script like this to a child node under your guard character:

```csharp
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.GodotConn;
using Dominatus.GodotConn.Actuation;
using Godot;

public partial class GuardBrain : DominatusAgentNode
{
    private static readonly StateId Patrol = StateId.Of("Patrol");
    private static readonly BbKey<Vector2> PatrolVelocity = GodotBbKeys.Vector2("guard.patrol_velocity");

    protected override HfsmGraph ConfigureGraph()
    {
        var graph = new HfsmGraph { Root = Patrol };
        graph.Add(Patrol, PatrolNode);
        return graph;
    }

    protected override void ConfigureActuators(ActuatorHost actuators)
    {
        actuators.Register(new Move2DActuationHandler((Node2D)GetParent(), WorldNode));
    }

    protected override void ConfigureBlackboard(Blackboard blackboard)
    {
        blackboard.Set(PatrolVelocity, new Vector2(60f, 0f));
    }

    private static IEnumerator<AiStep> PatrolNode(AiCtx ctx)
    {
        while (true)
        {
            var velocity = ctx.Bb.GetOrDefault(PatrolVelocity, new Vector2(60f, 0f));
            yield return new Act(new Move2DCommand(velocity));
            yield return null!;
        }
    }
}
```

## Optional explicit world binding

If your world node is not an ancestor and you are not using the `DominatusWorld` autoload name, set `WorldPath` in the inspector.

Discovery order is:

1. `WorldPath`
1. nearest ancestor `DominatusWorldNode`
1. `/root/DominatusWorld`

If none resolves, the node throws a clear error in `_Ready()`.

## Optional animation handler

If your character has an `AnimationPlayer`, register the animation handler too:

```csharp
protected override void ConfigureActuators(ActuatorHost actuators)
{
    actuators.Register(new Move2DActuationHandler((Node2D)GetParent(), WorldNode));
    actuators.Register(new PlayAnimationActuationHandler(GetNode<AnimationPlayer>("../AnimationPlayer")));
}
```

Then emit animation commands from a frame:

```csharp
yield return new Act(new PlayAnimationCommand("walk"));
```

## Mailbox bridge

From Godot code, you can inject a typed event into the local Dominatus mailbox surface:

```csharp
SendMessage(new PlayerSpottedMessage());
```

To send to another Dominatus agent in the same world:

```csharp
SendMessageTo(otherAgentId, new AlertMessage());
```

M0 also emits:

- `DominatusMessageSent(string messageType)`
- `DominatusStateChanged(string oldState, string newState)`

## Manual ticking

If you need manual ownership, set the world node tick mode to `Manual` and call:

```csharp
worldNode.Tick(deltaSeconds);
```

Agents should still register through `DominatusWorldNode`; only the ticking ownership changes.

## Blackboard guidance

Use Godot values directly where convenient:

```csharp
private static readonly BbKey<Vector2> LastSeenPosition = GodotBbKeys.Vector2("guard.last_seen_position");
private static readonly BbKey<NodePath> TargetPath = GodotBbKeys.NodePath("guard.target_path");
```

If you store a `NodePath`, resolve it when needed:

```csharp
if (Bb.TryResolveNode(TargetPath, this, out Node2D? target))
{
    GD.Print(target.Name);
}
```

Prefer `NodePath` for durable references. Treat live node references as runtime-only convenience values.

## Troubleshooting

### "Could not find a DominatusWorldNode"

- set `WorldPath`, or
- make the world node an ancestor, or
- add an autoload named `DominatusWorld`

### Agent registers but nothing moves

- verify `DominatusWorldNode.TickMode` is `PhysicsProcess` or `Process`
- confirm the brain node actually entered `_Ready()`
- for `CharacterBody2D`, confirm the registered move handler targets the body node you expect

### State never changes

- check that the returned `HfsmGraph` includes the intended root state
- confirm your frame yields transitions or behavior steps instead of only waiting forever

### Package restores but the Godot project complains about managed version mismatch

- align the project's Godot-managed package baseline with the engine version in use
- M0/M1 are validated against the Godot 4.7 managed package family
