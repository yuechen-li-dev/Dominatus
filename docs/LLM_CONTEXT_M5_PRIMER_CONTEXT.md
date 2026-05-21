# LLM Context M5: `PRIMER.context`

## What `PRIMER.context` is

`PRIMER.context` is a first-class LLM context category for reusable authoring law: must-do guidance, restrictions, preferred patterns, review checklists, and good/bad examples.

It is not project state and not agent identity.

## Category boundaries

- `SOUL.context`: durable identity/values.
- `PROJECT.context`: project facts, decisions, milestones.
- `SESSION.context`: short-lived task/session memory.
- `AGENT.context`: role/persona-specific behavior.
- `PRIMER.context`: reusable language/domain/tool constraints and examples.

## Why primers exist

Primers keep LLM-authored code inside a safe, reviewable subset. For Rust, this means boring explicit Rust: owned data by default, short borrows, small clones when they improve clarity, concrete structs/enums before trait/generic theater, loops when clearer, and restricted unsafe/interior mutability/async spread.

## Rust primer dogfood outputs

Generated at:

- `artifacts/llm-context-dogfood/primers/rust/RUST.primer.context.json`
- `artifacts/llm-context-dogfood/primers/rust/RUST.primer.context`
- `artifacts/llm-context-dogfood/primers/rust/manifest.json`
- `artifacts/llm-context-dogfood/primers/rust/packets/rust-author.md`
- `artifacts/llm-context-dogfood/primers/rust/packets/rust-author.manifest.json`
- `artifacts/llm-context-dogfood/primers/rust/packets/rust-reviewer.md`
- `artifacts/llm-context-dogfood/primers/rust/packets/rust-reviewer.manifest.json`
- `artifacts/llm-context-dogfood/primers/rust/packets/rust-auditor.md`
- `artifacts/llm-context-dogfood/primers/rust/packets/rust-auditor.manifest.json`
- `artifacts/llm-context-dogfood/primers/rust/packets/PRIMER_REVIEW_PROMPT.md`

## Loadouts

- `rust-author`: implementation authoring packet.
- `rust-reviewer`: subset-violation review packet.
- `rust-auditor`: footgun-focused audit packet (unsafe/interior mutability/async/borrow pretzels).

Loadouts intentionally differ in included chunk kinds and required chunk IDs.

## How to use primer packets

Use primer packets as constraints for implementation and review. They are reusable and should be applied alongside project-specific `PROJECT.context` and task-local `SESSION.context` packets.

## Parser scope

M5 hand-maps representative Rust primer content into chunks. No generic TOON importer/parser is added yet.

## Future work

Importer tooling can later automate controlled conversion from structured primer source files into `PRIMER.context` stores.
