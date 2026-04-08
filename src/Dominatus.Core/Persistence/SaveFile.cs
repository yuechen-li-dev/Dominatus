using System.Buffers.Binary;

namespace Dominatus.Core.Persistence;

/// <summary>
/// Flat binary save file: [magic(4)] [version(4)] [chunkCount(4)]
///   then for each chunk: [chunkIdLen(2)] [chunkId(utf8)] [payloadLen(4)] [payload(bytes)]
/// </summary>
public static class SaveFile
{
    private static readonly byte[] Magic = "DOM1"u8.ToArray();
    private const int FileVersion = 1;

    public static void Write(string path, IReadOnlyList<SaveChunk> chunks)
    {
        using var fs = File.Open(path, FileMode.Create, FileAccess.Write);
        using var w  = new BinaryWriter(fs);

        w.Write(Magic);
        w.Write(FileVersion);
        w.Write(chunks.Count);

        foreach (var chunk in chunks)
        {
            var idBytes = System.Text.Encoding.UTF8.GetBytes(chunk.Id.Value);
            w.Write((ushort)idBytes.Length);
            w.Write(idBytes);
            w.Write(chunk.Payload.Length);
            w.Write(chunk.Payload);
        }
    }

    public static List<SaveChunk> Read(string path)
    {
        using var fs = File.OpenRead(path);
        using var r  = new BinaryReader(fs);

        var magic = r.ReadBytes(4);
        if (!magic.SequenceEqual(Magic))
            throw new InvalidDataException("Not a Dominatus save file.");

        var fileVersion = r.ReadInt32(); // reserved for future migration
        var count       = r.ReadInt32();

        var chunks = new List<SaveChunk>(count);
        for (int i = 0; i < count; i++)
        {
            var idLen   = r.ReadUInt16();
            var idStr   = System.Text.Encoding.UTF8.GetString(r.ReadBytes(idLen));
            var payLen  = r.ReadInt32();
            var payload = r.ReadBytes(payLen);
            chunks.Add(new SaveChunk(new ChunkId(idStr), payload));
        }

        return chunks;
    }
}
