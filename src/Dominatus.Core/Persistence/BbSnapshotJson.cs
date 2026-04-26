namespace Dominatus.Core.Persistence;

public sealed record BbSnapshotJson(int v, BbEntryJson[] entries);

public sealed record BbEntryJson(string k, string t, object v, float? exp = null);
