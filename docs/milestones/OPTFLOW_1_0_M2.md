# OPTFLOW-1.0-M2 — Stable Ariadne operation identity

M2 introduces explicit, inspectable Ariadne dialogue IDs. Explicit IDs derive BB bookkeeping keys directly and are independent of caller file and line. The existing dispatch/pending/completion lifecycle, replay format, checkpoint schema, handlers, and HFSM states are unchanged.

Legacy source-derived overloads remain available with an obsolete migration warning. Their historical saves are not remapped; explicit alias migration is intentionally deferred.
