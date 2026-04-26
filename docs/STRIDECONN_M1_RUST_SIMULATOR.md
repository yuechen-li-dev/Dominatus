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

## M1.2 dialogue-path proof + robust UI attachment

M1.2 focuses on proving the command path in the running sandbox and making UI attachment more robust without relying on manual scene asset edits.

### What changed in M1.2

- `StrideDialogueSurface` now supports a caller-provided `UIComponent` and can also self-host a dedicated child entity with a `UIComponent` when none exists. If the installer entity is already in a scene, the UI host is explicitly added to that scene; otherwise it falls back to attaching UI directly on the installer entity.
- `EnsureInitialized()` writes explicit UI diagnostics (attach result, enabled state, page/root presence, root alignment/visibility).
- A persistent status text is shown immediately after initialize, independent of dialogue actuation state.
- Dialogue panel visibility now toggles independently of the root UI component so status remains visible while idle.
- `InstallDominatusRustSimulator` now emits one-time startup breadcrumbs and updates visible status text through startup.
- Added diagnostic startup option:
  - `ShowStartupSmokeLine` (default `true`) for explicit startup status text: `Dominatus Stride dialogue surface initialized.`

### Runtime breadcrumbs (expected logs)

At startup:

- `InstallDominatusRustSimulator.Start entered`
- `StrideDominatusSystem created` or `StrideDominatusSystem found`
- `runtime resolved`
- `surface initialized`
- `handlers registered`
- `graph registered`
- `agent added`
- `Update active`

During dialogue actuation:

- `TryShowLine called`
- `TryShowChoose called`
- `TryShowAsk called`
- `line completed`
- `choice completed`
- `ask completed`

### Troubleshooting

#### Where to look for logs

- Run the sandbox from Stride Game Studio or via `dotnet run` and watch standard output.
- Search for `[Dominatus.StrideSandbox]` and `[Dominatus.StrideConn]` prefixes.

#### What visible startup/status text should appear

At minimum, one or more of the following should be visible in the game window status overlay:

- `Dominatus installer started`
- `Dominatus world ticked`
- `RustSimulator agent added`
- `Waiting for first dialogue...`
- `DiagLine received: ...`

#### Startup status appears, but RustSimulator line does not

This usually means UI bootstrapping succeeded but RustSimulator did not emit (or did not reach) `DiagLineCommand` yet.
Use breadcrumbs to verify ordering through `graph registered`, `agent added`, and first `TryShowLine called`.

#### Logs show `TryShowLine called`, but no UI appears

This strongly indicates a rendering/attachment issue rather than command dispatch.
Check UI diagnostics for:

- `UIComponent attached: true`
- `UIComponent.Enabled: true`
- `UIComponent.Page != null: true`
- `UIPage.RootElement != null: true`

If those are all true and line logs fire, inspect scene/game compositor/UI rendering configuration next.

#### Scene asset editing note

Stride scene asset YAML is fragile; M1 avoids hand-editing scene YAML wherever possible.
Runtime script wiring and runtime UI host attachment are preferred for this milestone.
