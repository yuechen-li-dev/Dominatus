namespace Dominatus.Core.Persistence;

public sealed record ReplayLog(
    int Version,
    ReplayEvent[] Events);