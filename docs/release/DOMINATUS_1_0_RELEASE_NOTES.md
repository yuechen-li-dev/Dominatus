# Dominatus 1.0 release notes

Dominatus 1.0 freezes the deterministic kernel: explicit HFSM execution, utility arbitration, typed actuation, durable operation sites, structured child returns, deterministic transitions, source-generated flows, inspection/tracing, checkpoint/replay, and compatibility UtilityLite authoring.

The release includes Core, OptFlow, UtilityLite, Ariadne OptFlow, and the approved standard, home-assistant, audio, and payments actuator packages. Generated authoring is preferred; manual flow definitions remain supported. LLM integrations remain preview and are not part of this release set.

Known limitations: persistence supports codec-approved values rather than arbitrary objects; completed-result caching is deferred; one generated flow is supported per top-level static partial type; iterator bodies are not graph-analysed; retries and compensation are explicit; checkpoints reject an unconsumed child-return boundary; the runtime is not hard real-time; detailed tracing may allocate substantially; and robotics samples are experimental simulation only.
