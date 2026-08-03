# OptFlow 1.0 M6a — source-generated flow construction

M6a removes construction synchronization hazards while keeping control behavior explicit. `DominatusFlowAttribute` and `DominatusStateAttribute` are public OptFlow APIs. The incremental generator implements a declaration-only partial factory, emits `States.*` identities, root wiring, ordered registration, and normal runtime options.

Supported containers are partial static non-generic types with one static non-generic declaration-only `FlowDefinition` factory. States are static non-generic `IEnumerator<AiStep>` methods with `AiCtx` first and either no additional parameters or the complete factory parameter list. Unsupported shapes produce stable DOMFLOW diagnostics rather than being silently skipped.

The quadcopter proves static construction; the Home Assistant thermostat proves explicit policy capture. Ariadne and broader migrations, package delivery, and any grouping semantics for multiple factories remain M6b work.
