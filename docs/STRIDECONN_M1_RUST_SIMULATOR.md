# STRIDECONN M1: Rust Simulator dialogue-box integration

## Purpose

M1 demonstrates a playable Dominatus + Ariadne.OptFlow dialogue loop inside the existing Stride sandbox sample at:

- `samples/Dominatus.StrideSandbox`

This M1 intentionally keeps UI and scene wiring minimal so we can prove the runtime path end-to-end.

## What this is (and is not)

This is **current Dominatus architecture**, not the old Ariadne MVP stack.

Included path:

1. `StrideDominatusSystem` ticks `AiWorld`.
2. A sandbox `AiAgent` runs Rust Simulator HFSM.
3. Rust Simulator yields `Diag.Line`, `Diag.Choose`, `Diag.Ask` steps.
4. `ActuatorHost` dispatches `Diag*Command` commands.
5. `StrideDialogueActuationHandler` sends those to `StrideDialogueSurface`.
6. Stride UI interaction completes each actuation and resumes the HFSM.

Not included:

- Yarn compiler/loading.
- Old Ariadne runner/component ownership model.
- LLM/provider routing.
- InputMan.

## Project and sample wiring

### Reusable bridge (`src/Dominatus.StrideConn`)

Added reusable dialogue bridge types:

- `IStrideDialogueSurface`
- `StrideDialogueState`
- `StrideDialogueActuationHandler`
- `StrideDialogueSurface`

`StrideDialogueActuationHandler` handles:

- `DiagLineCommand`
- `DiagChooseCommand`
- `DiagAskCommand`

and completes via `ActuatorHost.CompleteLater(...)` once UI input arrives.

### Sandbox sample (`samples/Dominatus.StrideSandbox`)

- Added a sandbox-local copy of Rust Simulator under:
  - `Dominatus.StrideSandbox/Scripts/RustSimulator.cs`
- Added startup installer script:
  - `Dominatus.StrideSandbox/Scripts/InstallDominatusRustSimulator.cs`

Installer behavior:

1. Ensures `StrideDominatusSystem` exists in `Game.GameSystems`.
2. Resolves `IDominatusStrideRuntime` from `Game.Services`.
3. Creates `StrideDialogueSurface` and registers `StrideDialogueActuationHandler`.
4. Builds `HfsmGraph`, sets root to `Root`, and calls `RustSimulator.Register(graph)`.
5. Creates an `AiAgent` and adds it to `runtime.World`.

## Dialogue command mapping

### Line

- Shows speaker (when provided) and text.
- Advance by button click or Space/Enter.

### Choose

- Shows prompt and option buttons.
- Clicking a button completes payload with selected option key.
- Keyboard shortcuts `1`..`9` are supported.

### Ask

- Shows prompt and editable text input (`EditText`) with Submit.
- Press Enter to submit current text.
- Includes fallback button to submit `drop(player);` for MVP playability.

## Running the sandbox

1. Open `samples/Dominatus.StrideSandbox/Dominatus.StrideSandbox.sln` in Stride Game Studio or build via `dotnet`.
2. Attach `InstallDominatusRustSimulator` script to an entity in `Assets/MainScene.sdscene` (manual step in M1).
3. Run the game.

## Known limitations (M1)

- Scene asset was not auto-edited; script attachment is manual.
- UI is intentionally minimal and not styled/polished.
- Ask input is functional but includes fallback quick-answer button for rapid testing.

## M1.1 visibility + diagnostics update

M1.1 makes the dialogue surface explicitly visible and self-diagnosing in runtime logs.

### Visibility/layout changes

- `StrideDialogueSurface` now initializes a full-screen root `Grid` with explicit stretch alignment and an obvious tinted background.
- The dialogue panel is explicitly bottom-aligned with a fixed height and opaque background.
- Speaker/body/prompt text uses larger, high-contrast colors and wrapping.
- Choice buttons and ask buttons use explicit readable text styling.

### Runtime breadcrumbs (expected logs)

At startup in sandbox logs, expect lines similar to:

- `InstallDominatusRustSimulator.Start entered`
- `StrideDominatusSystem existing` or `StrideDominatusSystem created`
- `IDominatusStrideRuntime resolved`
- `StrideDialogueSurface initialized`
- `Ariadne dialogue handlers registered`
- `RustSimulator graph registered`
- `AiAgent added`
- `InstallDominatusRustSimulator.Update active` (one-time on first update)

During dialogue command actuation, expect:

- `TryShowLine called with speaker/text`
- `TryShowChoose called with prompt/option count`
- `TryShowAsk called with prompt`

### HFSM root-frame option note

M1.1 uses `new HfsmInstance(graph)` (default `KeepRootFrame = false`) for Rust Simulator.
This is intentional because Rust Simulator `Root` is a bootstrap flow state, not a root-frame overlay planner.

### Completion behavior note

`StrideDialogueActuationHandler` continues to complete dialogue commands via:

- `host.CompleteLater(ctx, id, ctx.World.Clock.Time, ...)`

This remains the intended M1 pattern; tests verify completion is observed on the next actuator/world tick.

### Linux build limitation note

- The broad Stride sandbox solution can include Windows launcher targets (`net10.0-windows`) that are not buildable on Linux.
- For M1.1 validation on Linux, use the narrow game project and test projects (`net10.0`) as the source of truth.

## Manual verification checklist (Stride Game Studio on Windows)

1. Open `samples/Dominatus.StrideSandbox/Dominatus.StrideSandbox.sln` in Stride Game Studio on Windows.
2. Ensure `MainScene` has an entity with `InstallDominatusRustSimulator` attached.
3. Run the game.
4. Confirm the first Rust Simulator line appears in a visible dialogue box:
   - `2:13 AM. The office is empty except for you, a flickering monitor, and a build that refuses to forgive.`
5. Press Space/Enter or click **Next** to advance.
6. Confirm choices appear and clickable buttons advance the story.
7. Confirm Ask step accepts typed input or the `drop(player);` fallback button.
