# Dominatus V1 M1e — Opt-in live smoke harness + provider demo mode

## Purpose

M1e adds an **opt-in** live smoke harness for `Dominatus.Llm.OptFlow` so the existing provider adapters can be exercised manually without changing authoring semantics.

Authoring remains:

```csharp
yield return Llm.Text(...);
```

Runtime flow remains sovereign:

```text
Llm.Text(...)
-> LlmTextRequest
-> Dominatus actuation
-> LlmTextActuationHandler
-> ILlmClient provider
-> JsonLlmCassette
-> replay/strict without live provider
```

## Demo client modes

`samples/Dominatus.Llm.DemoConsole` now supports:

- `--client fake` (default)
- `--client openai`
- `--client anthropic`
- `--client gemini`

Optional model override:

- `--model <model>`

Existing mode/cassette arguments remain:

- `--mode live|record|replay|strict|strict-miss`
- `--cassette <path>`

## Environment variables

Live/record for real providers is key-gated by environment variable:

- OpenAI: `OPENAI_API_KEY`
- Anthropic: `ANTHROPIC_API_KEY`
- Gemini: `GEMINI_API_KEY` (preferred), fallback `GOOGLE_API_KEY`

Replay/strict with cassette hit does not require a key.

## Safety policy

- No keys in source, logs, or cassettes.
- Demo only prints `ApiKeyPresent: true|false`.
- Live calls are opt-in (`--client` + `--mode live|record`).
- Replay/strict can run keyless when a cassette hit exists.
- No SDKs introduced.
- No streaming, tools, memory layer, or orchestration sprawl introduced.

## Example commands

### Default fake mode

```bash
dotnet run --project samples/Dominatus.Llm.DemoConsole -f net10.0 -- --client fake --mode record --cassette artifacts/llm/fake.oracle.cassette.json
dotnet run --project samples/Dominatus.Llm.DemoConsole -f net10.0 -- --client fake --mode replay --cassette artifacts/llm/fake.oracle.cassette.json
```

### OpenAI

```bash
dotnet run --project samples/Dominatus.Llm.DemoConsole -f net10.0 -- --client openai --model gpt-5 --mode record --cassette artifacts/llm/openai.oracle.cassette.json
unset OPENAI_API_KEY
dotnet run --project samples/Dominatus.Llm.DemoConsole -f net10.0 -- --client openai --model gpt-5 --mode replay --cassette artifacts/llm/openai.oracle.cassette.json
```

### Anthropic

```bash
dotnet run --project samples/Dominatus.Llm.DemoConsole -f net10.0 -- --client anthropic --model claude-sonnet-4-20250514 --mode record --cassette artifacts/llm/anthropic.oracle.cassette.json
unset ANTHROPIC_API_KEY
dotnet run --project samples/Dominatus.Llm.DemoConsole -f net10.0 -- --client anthropic --model claude-sonnet-4-20250514 --mode replay --cassette artifacts/llm/anthropic.oracle.cassette.json
```

### Gemini

```bash
dotnet run --project samples/Dominatus.Llm.DemoConsole -f net10.0 -- --client gemini --model gemini-2.5-flash --mode record --cassette artifacts/llm/gemini.oracle.cassette.json
unset GEMINI_API_KEY
unset GOOGLE_API_KEY
dotnet run --project samples/Dominatus.Llm.DemoConsole -f net10.0 -- --client gemini --model gemini-2.5-flash --mode replay --cassette artifacts/llm/gemini.oracle.cassette.json
```

## Tests and CI posture

Default test commands stay deterministic, keyless, and network-free.

Required commands:

```bash
dotnet test tests/Dominatus.Llm.OptFlow.Tests/Dominatus.Llm.OptFlow.Tests.csproj -f net8.0
dotnet test tests/Dominatus.Llm.OptFlow.Tests/Dominatus.Llm.OptFlow.Tests.csproj -f net10.0
dotnet test Dominatus.slnx
```

## Next recommended milestone

M1f: add optional, clearly-labeled manual smoke scripts/wrappers (still opt-in) that automate record-then-keyless-replay validation per provider while preserving network-free default tests.
