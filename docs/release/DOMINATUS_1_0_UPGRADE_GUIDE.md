# Dominatus 1.0 upgrade guide

Use source-generated OptFlow authoring for new flows; manual graph construction remains supported. Install `Dominatus.UtilityLite` only for compatibility—its canonical implementation is delivered by OptFlow. Align all 1.0 package references to `1.0.0`.

Give durable operations explicit operation-site IDs and Ariadne operations explicit IDs. Model child outcomes with `Pop`, `Succeed`, `Fail`, and `MatchReturn`; do not infer a return from mutable ambient state. Resolve generator diagnostics rather than suppressing them. Persistence compatibility is structural through current checkpoint codec semantics; no historical binary fixture compatibility is claimed.
