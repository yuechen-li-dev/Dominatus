using System.Text;

namespace Dominatus.Core.Persistence;

public sealed class SaveWriteContext
{
    private readonly List<SaveChunk> _chunks = new();

    public IReadOnlyList<SaveChunk> Chunks => _chunks;

    public void Add(ChunkId id, byte[] payload) => _chunks.Add(new SaveChunk(id, payload));

    public void AddUtf8Json(ChunkId id, string jsonUtf8)
        => Add(id, Encoding.UTF8.GetBytes(jsonUtf8));
}