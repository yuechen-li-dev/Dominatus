namespace Dominatus.Core.Persistence;

internal sealed record DominatusSaveMetaJson(
    string format,
    int v,
    int checkpointVersion);
