# OPTFLOW-1.0-M4 — Durable generic operation sites

M4 adds explicit `Operation.Site` descriptors and `Ai.Perform`. The implementation is a pending-only durable substrate: stable site identity, deterministic blackboard keys, dispatch once, restore/replay waiting, typed runtime completion delivery, and cleanup for later reuse. It adds zero HFSM states and does not change the checkpoint or replay formats.

`DurablePrimitiveResult` is intentionally not implemented. Durable completed results require a run/generation discriminator so ordinary reuse cannot be mistaken for cold re-entry. That belongs to M5 alongside any explicit result-codec design. M4 also defers retries, backoff, compensation, result routing, workflow graphs, cancellation policy, and domain-wrapper consolidation.

The package remains pre-1.0; no release version is implied by this milestone.
