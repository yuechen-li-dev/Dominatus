# Dominatus.Actuators.Standard M0

## Purpose

`Dominatus.Actuators.Standard` is a non-LLM standard actuator package for practical local automation with typed command handlers.

It provides:

- Sandboxed text file commands.
- Basic file query/list commands.
- Wall-clock time commands with injectable clock.

It does **not** provide LLM, network, process/shell, email, Telegram, MCP, secret handling, or scheduling features.

## Package name

- `Dominatus.Actuators.Standard`

## Design boundary

This package is **not a formal OS sandbox**. It intentionally enforces sane boundaries:

- Allowed file roots are explicit and named.
- Commands use `Root + relative Path`.
- Absolute command paths are denied.
- Path traversal is denied.
- Known dangerous broad roots are rejected by default.
- Denials are deterministic and inspectable.

## File commands

- `ReadTextFileCommand`
- `WriteTextFileCommand`
- `AppendTextFileCommand`
- `FileExistsCommand`
- `ListFilesCommand`

All file commands are resolved through named sandbox roots.

### Path behavior

- Unknown root: fail.
- Empty path: fail for file commands, allowed for list root traversal.
- Absolute path: fail.
- Traversal (`../`, `..\`) or any escape outside root: fail.
- Containment check avoids prefix bugs (e.g. `/tmp/root2` is not under `/tmp/root`).

### Dangerous root rejection

The options validator rejects obvious broad directories (for example `/`, `/etc`, `/usr`, `C:/`, `C:/Windows`, `C:/Program Files`, `C:/Users`) and also rejects the current user's home directory if configured exactly as a root.

## Time commands

- `GetUtcNowCommand`
- `GetLocalNowCommand`

Returned payload type:

- `TimeResult`

### Determinism note

Use `AiWorld.Clock` for deterministic simulation time. Use these time actuators only when true wall-clock time is explicitly needed, because wall-clock time is nondeterministic external input.

## Registration

```csharp
var options = new SandboxedFileActuatorOptions
{
    Roots = new[]
    {
        new SandboxedFileRoot("workspace", @"C:\Projects\MyGame")
    }
};

var host = new ActuatorHost();
host.Register(new SandboxedFileActuationHandler(options));
host.Register(new TimeActuationHandler());
```

Optional helpers:

```csharp
host.RegisterStandardFileActuators(options);
host.RegisterStandardTimeActuators();
```

## ActuatorHost usage example

```csharp
yield return Ai.Act(
    new WriteTextFileCommand("workspace", "notes/todo.txt", "Remember to hydrate.", overwrite: true),
    LastWriteId);

yield return Ai.Await(LastWriteId, LastWriteResult);
```

`Ai.Act` / `Ai.Await` are authoring helpers from `Dominatus.OptFlow`. They are not required by this actuator package itself.


## Related docs

- [M1 HTTP allowlisted actuators](ACTUATORS_STANDARD_M1_HTTP.md)

## Non-goals (M0)

- Formal OS sandbox.
- LLM integrations.
- HTTP/network actuators.
- Shell/process execution.
- Email/Telegram/MCP integrations.
- Secret management.
- Background scheduling/reminder systems.
