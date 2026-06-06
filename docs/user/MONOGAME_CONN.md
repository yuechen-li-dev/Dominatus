# Dominatus.MonoGameConn M0

`Dominatus.MonoGameConn` is a thin MonoGame adoption bridge for Dominatus. It lets an existing MonoGame game tick an `AiWorld` from the MonoGame update loop and share a few SpriteBatch-friendly blackboard conventions without introducing a new engine layer.

## Doctrine

- MonoGame owns the game loop, rendering, input, content, windows, and platform details.
- Dominatus owns behavior/control: agents, blackboards, HFSMs, decisions, events, and typed actuation boundaries.
- Game code owns entities, sprites, textures, physics choices, content loading, and how agent blackboard facts map to visible objects.
- `Dominatus.MonoGameConn` does not provide an ECS, scene graph, renderer abstraction, sprite manager, physics layer, content pipeline wrapper, editor, network layer, LLM calls, or runtime behavior changes in `Dominatus.Core`.

## Install/package reference

For a project reference while developing in this repository:

```xml
<ProjectReference Include="..\..\src\Dominatus.MonoGameConn\Dominatus.MonoGameConn.csproj" />
```

For a package reference once published:

```xml
<PackageReference Include="Dominatus.MonoGameConn" Version="0.2.0-preview" />
```

The connector references `MonoGame.Framework.DesktopGL` because the existing FishTank MonoGame sample uses that package. It intentionally avoids `MonoGame.Content.Builder.Task`; your game can add content build tooling if it needs `.mgcb` assets.

## Ticking Dominatus from MonoGame

Create your normal `AiWorld`, add your agents, then add `DominatusGameComponent` to the MonoGame component list:

```csharp
using Dominatus.Core.Runtime;
using Dominatus.MonoGameConn;

public sealed class MyGame : Game
{
    private readonly AiWorld _world = new();

    protected override void Initialize()
    {
        Components.Add(new DominatusGameComponent(this, _world));
        base.Initialize();
    }
}
```

`DominatusGameComponent` calls `world.Tick(dt)` during `Update(GameTime)`, where `dt` is `gameTime.ElapsedGameTime.TotalSeconds * TimeScale`.

It exposes:

- `World` — the owned/ticked `AiWorld` reference supplied by game code.
- `IsPaused` — when true, `Update` skips ticking Dominatus.
- `TimeScale` — finite, non-negative multiplier for elapsed game time.
- `UpdatesProcessed` — increments only when a tick occurs.
- `LastDeltaSeconds` — the last delta passed to `AiWorld.Tick`.

## Fixed timestep note

MonoGame can run with `IsFixedTimeStep` enabled or disabled. `DominatusGameComponent` does not impose a separate fixed-step simulation; it uses the elapsed `GameTime` MonoGame provides. If you need accumulator-based fixed AI steps, keep that policy in your game for now. A future helper may add this without changing the M0 thin-bridge contract.

## Blackboard key conventions

`MonoGameBbKeys` contains optional conventions for common game-rendering facts:

```csharp
MonoGameBbKeys.Position    // BbKey<Vector2>("monogame.position")
MonoGameBbKeys.Velocity    // BbKey<Vector2>("monogame.velocity")
MonoGameBbKeys.Rotation    // BbKey<float>("monogame.rotation")
MonoGameBbKeys.DebugLabel  // BbKey<string>("monogame.debug_label")
MonoGameBbKeys.Visible     // BbKey<bool>("monogame.visible")
```

These keys are conventions only. Dominatus does not require them, and you can define your own keys for your own entities.

Example agent setup:

```csharp
agent.Bb.Set(MonoGameBbKeys.Position, new Vector2(100, 120));
agent.Bb.Set(MonoGameBbKeys.Velocity, new Vector2(20, 0));
agent.Bb.Set(MonoGameBbKeys.DebugLabel, "Patrol");
```

## SpriteBatch rendering example

Rendering stays in your `Draw` method. The connector does not call `SpriteBatch.Begin` or draw sprites for you:

```csharp
protected override void Draw(GameTime gameTime)
{
    GraphicsDevice.Clear(Color.CornflowerBlue);

    _spriteBatch.Begin();

    foreach (var agent in _world.Agents)
    {
        if (!agent.Bb.GetOrDefault(MonoGameBbKeys.Visible, true))
            continue;

        if (agent.Bb.TryGet(MonoGameBbKeys.Position, out var pos))
            _spriteBatch.Draw(_texture, pos, Color.White);
    }

    _spriteBatch.End();

    base.Draw(gameTime);
}
```

## Debug overlay helper

`DebugAgentOverlay` can build label text and projected label positions without requiring a graphics device, so tests and headless tooling can validate the projection path:

```csharp
var labels = DebugAgentOverlay.BuildLabels(_world.Agents);
```

For runtime drawing, call it inside your existing SpriteBatch batch:

```csharp
_spriteBatch.Begin();
DebugAgentOverlay.Draw(_spriteBatch, _font, _world.Agents);
_spriteBatch.End();
```

Behavior:

- Reads `MonoGameBbKeys.Position`; skips agents without a position.
- Reads `MonoGameBbKeys.DebugLabel` when configured.
- Uses `DebugAgentOverlayOptions.Offset` to place text above the sprite by default.
- Can include agent IDs and, optionally, the current HFSM state path.
- Does not call `Begin`/`End`; the caller owns SpriteBatch batching.

## M0 sample status

No separate `samples/Dominatus.MonoGameConn.BasicAgents` project is added in M0. MonoGame samples often pull in content-pipeline/windowing concerns, and this milestone keeps CI and package smoke tests headless. The existing `samples/Dominatus.FishTank` remains the richer MonoGame example; an M1 visual sample can be added once sample content/build policy is settled.

## Future demo plan

A later visual demo can connect this bridge to RTSBenchmark-style agents, but M0 deliberately avoids an RTSBenchmark visualizer and any renderer abstraction. The goal here is a boring drop-in connector, not a game engine.
