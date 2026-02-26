namespace Dominatus.Core.Persistence;

public sealed record BbDeltaLogJson(int v, BbDeltaEntryJson[] entries);

public sealed record BbDeltaEntryJson(
    float ts,
    string k,
    string op,
    BbTypedValue? old,
    BbTypedValue? @new);

public sealed record BbTypedValue(string t, object v);