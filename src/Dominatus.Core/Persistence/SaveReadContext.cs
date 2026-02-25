using System.Text;

namespace Dominatus.Core.Persistence;

public sealed class SaveReadContext
{
    private readonly Dictionary<ChunkId, SaveChunk> _byId;

    public SaveReadContext(IEnumerable<SaveChunk> chunks)
    {
        _byId = new Dictionary<ChunkId, SaveChunk>();
        foreach (var c in chunks)
            _byId[c.Id] = c;
    }

    public bool TryGet(ChunkId id, out SaveChunk chunk) => _byId.TryGetValue(id, out chunk!);

    public bool TryGetUtf8Json(ChunkId id, out string json)
    {
        json = "";
        if (!TryGet(id, out var c)) return false;
        json = Encoding.UTF8.GetString(c.Payload);
        return true;
    }
}