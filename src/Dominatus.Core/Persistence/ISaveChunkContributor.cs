namespace Dominatus.Core.Persistence;

/// <summary>
/// Optional hook for host/domain to contribute additional chunks (world state, etc.).
/// Core never interprets these.
/// </summary>
public interface ISaveChunkContributor
{
    void WriteChunks(SaveWriteContext ctx);
    void ReadChunks(SaveReadContext ctx);
}