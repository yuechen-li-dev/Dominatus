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
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must be non-empty.", nameof(path));
        if (chunks is null)
            throw new ArgumentNullException(nameof(chunks));

        using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var w = new BinaryWriter(fs);

        w.Write(Magic);
        w.Write(FileVersion);
        w.Write(chunks.Count);

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chunk in chunks)
        {
            if (chunk is null)
                throw new InvalidOperationException("Save chunk list contains a null chunk.");

            var id = chunk.Id.Value;
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("Chunk id must be non-empty.");

            if (!seen.Add(id))
                throw new InvalidOperationException($"Duplicate chunk id '{id}' is not allowed.");

            var idBytes = System.Text.Encoding.UTF8.GetBytes(id);
            if (idBytes.Length > ushort.MaxValue)
                throw new InvalidOperationException($"Chunk id '{id}' is too long.");

            var payload = chunk.Payload ?? Array.Empty<byte>();

            w.Write((ushort)idBytes.Length);
            w.Write(idBytes);
            w.Write(payload.Length);
            w.Write(payload);
        }
    }

    public static List<SaveChunk> Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must be non-empty.", nameof(path));

        using var fs = File.OpenRead(path);
        using var r = new BinaryReader(fs);

        var magic = r.ReadBytes(4);
        if (magic.Length != 4 || !magic.SequenceEqual(Magic))
            throw new InvalidDataException("Not a Dominatus save file.");

        var fileVersion = r.ReadInt32();
        if (fileVersion != FileVersion)
            throw new InvalidDataException(
                $"Unsupported Dominatus file version. Expected {FileVersion}, got {fileVersion}.");

        var count = r.ReadInt32();
        if (count < 0)
            throw new InvalidDataException("Chunk count cannot be negative.");

        var chunks = new List<SaveChunk>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < count; i++)
        {
            var idLen = r.ReadUInt16();
            var idBytes = r.ReadBytes(idLen);
            if (idBytes.Length != idLen)
                throw new EndOfStreamException("Unexpected end of file while reading chunk id.");

            var idStr = System.Text.Encoding.UTF8.GetString(idBytes);
            if (string.IsNullOrWhiteSpace(idStr))
                throw new InvalidDataException("Encountered empty chunk id.");

            if (!seen.Add(idStr))
                throw new InvalidDataException($"Duplicate chunk id '{idStr}' found in save file.");

            var payLen = r.ReadInt32();
            if (payLen < 0)
                throw new InvalidDataException($"Chunk '{idStr}' has negative payload length.");

            var payload = r.ReadBytes(payLen);
            if (payload.Length != payLen)
                throw new EndOfStreamException(
                    $"Unexpected end of file while reading payload for chunk '{idStr}'.");

            chunks.Add(new SaveChunk(new ChunkId(idStr), payload));
        }

        if (fs.Position != fs.Length)
            throw new InvalidDataException("Save file contains trailing bytes after the final chunk.");

        return chunks;
    }
}
