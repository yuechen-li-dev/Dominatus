# LLM Context M0

Dominatus.Llm.Context models LLM context as explicit state chunks, not chat transcripts.

- **Context store**: a save-file-like set of versioned chunks.
- **Context packet**: deterministic generated working set for one model call.

M0 uses JSON (`dominatus.llm.context.store`, version `1`) for inspectable persistence.
Binary chunked `.context` is future work.

## Example JSON

```json
{"format":"dominatus.llm.context.store","version":1,"id":"PROJECT.dominatus","title":"Dominatus Project Context","createdUtc":"2026-01-01T00:00:00+00:00","updatedUtc":"2026-01-01T00:00:00+00:00","chunks":[]}
```


See also: `docs/LLM_CONTEXT_M1_LOADOUTS.md` for named loadouts (M1).


See also: `docs/LLM_CONTEXT_M2_CONTAINER.md` for the binary `.context` durable container layer.
