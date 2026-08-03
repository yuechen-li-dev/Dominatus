# Dominatus 1.0 M8 closeout

M8 fixes replay completion bookkeeping, records the allocation caveat, freezes the robotics experiment, and adds a package manifest, package-only smoke path, and manual non-publishing release workflow. Replay cleanup belongs to the completion dispatcher: `ReplayDriver` now removes exactly the restored agent obligation whose completion it injects, matching `ActuatorHost.Tick`.

The M7 measurement remains the baseline: Dominatus allocated approximately 39–54 MB per 900-tick traced scenario versus approximately 2.5 MB for the conventional fixture. The remaining gap is primarily diagnostic/trace and execution-model cost; no unsafe allocator redesign was started. Full diagnostic tracing remains available and should not be used for apples-to-apples runtime comparisons.
