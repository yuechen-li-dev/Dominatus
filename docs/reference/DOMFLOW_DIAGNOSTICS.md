# DOMFLOW diagnostics

All generator diagnostics are errors and point to authored declarations, never generated source. Each is covered by the Roslyn generator test suite.

| ID | Cause | Correction |
| --- | --- | --- |
| DOMFLOW001 | Factory is not partial | Declare `static partial FlowDefinition Define();`. |
| DOMFLOW002 | Factory does not return `FlowDefinition` | Return exactly `FlowDefinition`. |
| DOMFLOW003 | Container is not supported | Use a top-level partial static non-generic class. |
| DOMFLOW004 | No attributed state methods | Add at least one `[DominatusState("id")]` method. |
| DOMFLOW005 | No root state | Mark exactly one state `Root = true`. |
| DOMFLOW006 | Multiple root states | Keep one root annotation. |
| DOMFLOW007 | Durable ID repeats | Give every state a distinct explicit ID. |
| DOMFLOW008 | State signature is invalid | Return `IEnumerator<AiStep>` and take `AiCtx` first. |
| DOMFLOW009 | Factory parameter is unsupported | Use ordinary by-value non-ref-like parameters. |
| DOMFLOW010 | State method is generic | Make the annotated state non-generic. |
| DOMFLOW011 | Annotated state methods are overloaded | Give generated state members unique method names. |
| DOMFLOW012 | Authored `States` member conflicts | Remove or rename the authored member. |
| DOMFLOW013 | More than one flow factory exists | Use one generated flow per type. |
| DOMFLOW014 | Flow or state ID is blank | Supply a non-blank literal durable ID. |
| DOMFLOW015 | State parameters do not match factory | Use no extra parameters or the complete factory list. |
| DOMFLOW016 | Factory is not static | Declare the factory static. |
| DOMFLOW017 | State is not static | Declare the annotated state static. |
| DOMFLOW018 | Generated state member name collides | Use unique annotated state method names. |
| DOMFLOW019 | Container is generic | Move the flow into a non-generic container. |
| DOMFLOW020 | Factory has an authored implementation | Leave the partial factory declaration-only. |

For example, `[DominatusState(" ")]` produces `DOMFLOW014`; `[DominatusState("Root", Root = true)]` is the corrected explicit form. Iterator bodies, calls to `Ai.Goto`, and utility targets are deliberately not analyzed.
