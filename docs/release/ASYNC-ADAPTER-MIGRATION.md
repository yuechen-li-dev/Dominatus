# Async adapter migration qualification

Outcome: **Success**. Legacy sync-over-async actuator bridges now use the shared
Core async actuation lifecycle. This closes the remaining adapter boundary from
the native engineering-tools pass without adding a second scheduler.

## Repairs

- Migrated standard HTTP/process, Home Assistant, audio, all six payment commands,
  LLM streaming, decision scoring, Magi, and the thermostat template. Existing
  registration APIs and commands remain available. Direct callers await
  `HandleAsync`; state machines dispatch and suspend through their normal steps.
- Added owner-tick delivery for immutable progress events. Async continuations do
  not mutate the world, Blackboard or event bus. Progress precedes completion and
  is suppressed after cancellation/removal/disposal; late reports are ignored.
- Removed the process adapter's completed-output-task busy loop. Exit and both
  bounded pipe readers stay owned and observed. Cancellation, timeout and output
  overflow terminate the process tree; cleanup awaits exit and readers.
- Extended HTTP/Home Assistant deadlines through response-body reads, retaining
  request/response byte budgets and existing validation/status semantics.
- Added explicit adapter ownership to registration. HTTP/Home Assistant factories
  transfer their created adapters to the host; caller-injected transports remain
  borrowed. DisposeAsync awaits work before releasing owned adapter resources.
- Kept both Magi advocates owned even when a provider throws synchronously before
  returning a task. The judge runs only after both valid results and a cancellation
  check. Existing cassette modes, result validation and option scoring remain.
- LLM helper steps now consume authoritative completion events. Runtime failures
  without typed payloads no longer strand suspended steps. Failed/canceled
  streams report failure while retaining partial-text snapshots when available.

## Contract and compatibility

[C# await and agent suspension](../actuators/ASYNC-ACTUATION.md) is the current
execution guide. README and package guides link to it; the obsolete synchronous
Home Assistant description is corrected.

An accepted, incomplete dispatch is not a successful operation result. Consumers
must keep ticking and wait for completion. Cached/immediate providers can still
complete synchronously. Handler unit tests now await the direct TAP entrypoint;
host tests exercise both immediate and genuinely pending providers.

`Register(handler)` remains borrowed. The ownership overload is opt-in; existing
callers retain their lifecycle responsibilities. No provider API, payment retry,
idempotency key, model-selection policy or cassette schema was replaced.

No running Task is checkpointed or durably resumed. Existing operation-site and
cassette contracts still own persistence. Caller cancellation cannot undo an
external service's accepted write or forcibly terminate noncooperative managed
code. Native process termination remains subject to operating-system permissions;
this is not a new process sandbox. No live provider inference/payment claim is made.

## Qualification

Serial Release builds/tests used `-m:1 -nr:false -p:UseSharedCompilation=false`
and `RunConfiguration.MaxCpuCount=1`.

| Lane | Passed | Skipped | Target suites |
| --- | ---: | ---: | ---: |
| Dominatus.Release.slnx | 1,453 | 8 opt-in live Stripe cases | 18 |
| Llm.OptFlow.Tests | 1,424 (712 each on net8/net10) | 0 | 2 |
| Actuators.SemanticKernel.Tests | 140 | 0 | 2 |
| Template.HomeAssistantThermostat.Tests | 12 | 0 | 1 |
| Template.LlmPrReview.Tests | 11 | 0 | 1 |
| SemanticKernelOrchestration.Tests | 16 | 0 | 2 |
| SemanticKernelGraphAssistant.Tests | 8 | 0 | 1 |
| Dominatus.Server.Tests | 78 | 0 | 2 |
| Leviathan.Server.Tests against maintained Dominatus source | 64 | 0 | 1 |

Total: **3,206 passed executions**, zero failures, eight opt-in live tests skipped.
These are the full release-set suites plus the affected optional LLM/SK/server and
sample consumers, not every optional project in Dominatus.slnx. Aetheris geometry
code is unchanged; its kernel corpus is outside this adapter qualification.

New evidence includes gated pending HTTP/Home Assistant/audio/payment calls,
response-body timeout with synchronous reads forbidden, owner-tick progress,
late-progress suppression, cancellation/removal/disposal, resource ownership,
pending decision binding and canceled late results, and Magi synchronous-throw
sibling observation. Real .NET child-process tests record child IDs and verify
exit after cancellation, host shutdown and output overflow. Existing timeout,
nonzero-exit, output-budget, policy, replay and provider-mapping tests remain green.

A source audit found no remaining `GetAwaiter().GetResult()`, task `.Wait()`, or
synchronous `WaitForExit()` bridges in current adapters or the thermostat template.
Core retains one guarded `ValueTask.Result` read after `IsCompletedSuccessfully`;
that is an immediate-result path, not a blocking wait. Domain DTO properties named
`Result`, simulation `Ai.Wait`, and the console runner's pacing sleep are unrelated.

Local raw logs are in the companion Aetheris checkout's ignored `artifacts/local/`:
`dominatus-adapters-*.log` and `*-async.log`. No server was started or left running
for this pass. Final process inventory found zero remaining dotnet/testhost/compiler
hosts. Diff whitespace and guide/report relative links also passed inspection.
Existing analyzer warnings remain outside this repair.
