# OptFlow 1.0 M6b — packaged generator and real-flow migration

M6b ships the incremental generator inside the `Dominatus.OptFlow` package under `analyzers/dotnet/cs`. A normal package reference activates it automatically; it is compile-time only and is not copied into application output.

The milestone hardens all `DOMFLOW001`–`DOMFLOW020` diagnostics and migrates Ariadne Thread of Night plus TinyTown's utility-driven townie flow. Both retain explicit durable IDs, ordinary node bodies, FlowDefinition validation, fresh graph construction, and zero hidden states. Ariadne preserves dialogue IDs and routes; TinyTown preserves utility arbitration and steady action nodes.

M6b does not infer graph edges or inspect iterator bodies. Multiple generated flows per type and broader analyzer packaging/release automation remain future work.
