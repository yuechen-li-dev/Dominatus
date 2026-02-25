namespace Dominatus.Core.Persistence;

public sealed record SaveChunk(ChunkId Id, byte[] Payload);