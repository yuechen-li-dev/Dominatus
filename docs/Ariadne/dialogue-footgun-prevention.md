# Dialogue footgun prevention

This table records the M7a disposition. Raw OptFlow remains an intentional expert escape hatch.

| Hazard | Disposition | Mechanism |
|---|---|---|
| manual state registration / missing target | prevented by type/API and validation | immutable closed graph, indexed before lowering; missing call and loop targets are diagnostics |
| `Goto` used where `Push` was intended | prevented by type/API | high-level authoring exposes `Call`, not `Goto` or `Push` |
| hidden control flow in inline helpers | prevented by type/API | no author-supplied `IEnumerable<AiStep>` or continuation is accepted |
| root restart / keep-root confusion | prevented by type/API | lowerer owns one generated root and runtime options |
| tight loop without wait | prevented by type/API and validation | only explicit `LoopTo`; each loop traverses semantic operation states, and SCC analysis audits intent |
| accidental nontermination | prevented by validation | every structural cycle must contain an intentional loop edge |
| source-line operation IDs | prevented by type/API | typed authored local IDs derive deterministic operation IDs |
| concurrent operation-ID aliasing | prevented by validation | dialogue-local IDs and globally composed IDs must be unique |
| invalid choice result | prevented by runtime validation | selected payload must match the ordered options presented by that choice state |
| hidden iterator locals assumed durable | prevented by lowering | one resumable semantic operation per HFSM state |
| exact suspended-continuation assumption | documented and prevented by lowering | cold restore re-enters the active state path; iterator position is not persistence |
| repeated-menu stale pending state | prevented by runtime primitive | bookkeeping clears after completion; fresh waits start at the event tail |
| dynamic reachability opacity | prevented by validation | dialogue validates its closed structural graph before `FlowDefinition` lowering |
| persistent event re-consumption | prevented by API | high-level Dialogue exposes no persistent listener construct |
| call recursion | prevented by validation | recursive definition calls are rejected in M7a |
| arbitrary backward edge | raw OptFlow escape-hatch only | ordinary builder offers only explicit `LoopTo` |
| unrestricted HFSM composition | raw OptFlow escape-hatch only | use raw OptFlow when the closed dialogue model is intentionally insufficient |

No listed high-level hazard remains unresolved in M7a. General unrestricted control-flow safety remains the responsibility of authors who choose raw OptFlow.

