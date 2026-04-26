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
