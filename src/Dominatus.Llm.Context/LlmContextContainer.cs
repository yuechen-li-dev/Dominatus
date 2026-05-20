using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Dominatus.Llm.Context;

public sealed record LlmContextContainerChunkInfo(
    string Id,
    string Kind,
    string Format,
    int Version,
    long Offset,
    long Length,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record LlmContextContainerManifest(
    int FormatVersion,
    IReadOnlyList<LlmContextContainerChunkInfo> Chunks);

public static class LlmContextContainer
{
    public const int CurrentVersion = 1;
    public const string DefaultStoreChunkId = "context.store";
    public const string StoreChunkKind = "context-store";
    public const string StoreChunkFormat = "application/vnd.dominatus.llm.context.store+json";
    public const int StoreChunkVersion = 1;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("DCTX");
    private const int HeaderSize = 32;

    public static byte[] WriteToBytes(LlmContextStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var storeJson = LlmContextStoreJson.Serialize(store);
        var payloadBytes = Encoding.UTF8.GetBytes(storeJson);

        var chunk = new LlmContextContainerChunkInfo(
            DefaultStoreChunkId,
            StoreChunkKind,
            StoreChunkFormat,
            StoreChunkVersion,
            0,
            payloadBytes.LongLength,
            store.CreatedUtc,
            store.UpdatedUtc);

        var sorted = new[] { chunk }.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var directoryTemplate = sorted.Select(x => new ContainerChunkDirectoryEntry(x, 0, x.Length)).ToArray();
        var directoryBytes = SerializeDirectoryJson(directoryTemplate);
        var payloadStart = HeaderSize + directoryBytes.LongLength;

        var resolved = new[]
        {
            chunk with { Offset = payloadStart }
        };
        var resolvedDirectory = resolved.Select(x => new ContainerChunkDirectoryEntry(x, x.Offset, x.Length)).ToArray();
        directoryBytes = SerializeDirectoryJson(resolvedDirectory);
        payloadStart = HeaderSize + directoryBytes.LongLength;
        resolved = [chunk with { Offset = payloadStart }];
        resolvedDirectory = resolved.Select(x => new ContainerChunkDirectoryEntry(x, x.Offset, x.Length)).ToArray();
        directoryBytes = SerializeDirectoryJson(resolvedDirectory);

        using var ms = new MemoryStream(capacity: checked((int)(HeaderSize + directoryBytes.Length + payloadBytes.Length)));
        WriteHeader(ms, CurrentVersion, HeaderSize, resolved.Length, HeaderSize, directoryBytes.LongLength);
        ms.Write(directoryBytes);
        ms.Write(payloadBytes);
        return ms.ToArray();
    }

    public static LlmContextStore ReadStore(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var ms = new MemoryStream(bytes, writable: false);
        return Read(ms);
    }

    public static void Save(string path, LlmContextStore store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllBytes(path, WriteToBytes(store));
    }

    public static LlmContextStore Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ReadStore(File.ReadAllBytes(path));
    }

    public static LlmContextContainerManifest ReadManifest(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var ms = new MemoryStream(bytes, writable: false);
        return ReadManifest(ms);
    }

    public static LlmContextContainerManifest ReadManifest(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ReadManifest(File.ReadAllBytes(path));
    }

    public static void Write(Stream stream, LlmContextStore store)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var bytes = WriteToBytes(store);
        stream.Write(bytes);
    }

    public static LlmContextStore Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        var (header, entries) = Parse(bytes);
        _ = header;

        var storeEntry = entries.SingleOrDefault(x => string.Equals(x.Id, DefaultStoreChunkId, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Missing required chunk '{DefaultStoreChunkId}'.");

        if (!string.Equals(storeEntry.Format, StoreChunkFormat, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported store chunk format '{storeEntry.Format}'.");
        if (storeEntry.Version != StoreChunkVersion)
            throw new InvalidDataException($"Unsupported store chunk version '{storeEntry.Version}'.");

        var payload = bytes.AsSpan(checked((int)storeEntry.Offset), checked((int)storeEntry.Length));
        try
        {
            return LlmContextStoreJson.Deserialize(Encoding.UTF8.GetString(payload));
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            throw new InvalidDataException("Malformed store payload JSON.", ex);
        }
    }

    public static LlmContextContainerManifest ReadManifest(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        var (header, entries) = Parse(bytes);
        var chunks = entries.Select(x => new LlmContextContainerChunkInfo(x.Id, x.Kind, x.Format, x.Version, x.Offset, x.Length, x.CreatedUtc, x.UpdatedUtc)).ToArray();
        return new LlmContextContainerManifest(header.FormatVersion, chunks);
    }

    private static (ContainerHeader Header, ContainerChunkDirectoryEntry[] Entries) Parse(byte[] bytes)
    {
        if (bytes.Length < HeaderSize)
            throw new InvalidDataException("Container file is too short.");

        var header = ReadHeader(bytes);

        if (header.DirectoryOffset < HeaderSize || header.DirectoryLength < 0)
            throw new InvalidDataException("Invalid directory location in header.");

        if (header.DirectoryOffset + header.DirectoryLength > bytes.LongLength)
            throw new InvalidDataException("Directory extends outside file bounds.");

        ContainerDirectoryDto directory;
        try
        {
            var dirSpan = bytes.AsSpan(checked((int)header.DirectoryOffset), checked((int)header.DirectoryLength));
            directory = JsonSerializer.Deserialize(dirSpan, LlmContextJsonContext.Default.ContainerDirectoryDto)
                ?? throw new InvalidDataException("Directory json could not be parsed.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Malformed directory json.", ex);
        }

        var entries = directory.Chunks ?? [];
        if (entries.Length != header.ChunkCount)
            throw new InvalidDataException("Chunk count does not match header.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!seen.Add(entry.Id)) throw new InvalidDataException($"Duplicate chunk id '{entry.Id}'.");
            if (entry.Offset < 0 || entry.Length < 0) throw new InvalidDataException($"Chunk '{entry.Id}' has negative offset or length.");
            if (entry.Offset + entry.Length > bytes.LongLength) throw new InvalidDataException($"Chunk '{entry.Id}' extends outside file bounds.");
        }

        return (header, entries.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray());
    }

    private static byte[] SerializeDirectoryJson(ContainerChunkDirectoryEntry[] entries)
    {
        var dto = new ContainerDirectoryDto { Chunks = entries.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray() };
        return JsonSerializer.SerializeToUtf8Bytes(dto, LlmContextJsonContext.Default.ContainerDirectoryDto);
    }

    private static void WriteHeader(Stream stream, int version, int headerSize, int chunkCount, long directoryOffset, long directoryLength)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..8], version);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..12], headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..16], chunkCount);
        BinaryPrimitives.WriteInt64LittleEndian(header[16..24], directoryOffset);
        BinaryPrimitives.WriteInt64LittleEndian(header[24..32], directoryLength);
        stream.Write(header);
    }

    private static ContainerHeader ReadHeader(byte[] bytes)
    {
        if (!bytes.AsSpan(0, 4).SequenceEqual(Magic)) throw new InvalidDataException("Invalid container magic.");
        var version = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4));
        if (version != CurrentVersion) throw new InvalidDataException($"Unsupported container version '{version}'.");

        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4));
        if (headerSize != HeaderSize) throw new InvalidDataException($"Unsupported header size '{headerSize}'.");
        var chunkCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12, 4));
        if (chunkCount < 0) throw new InvalidDataException("Chunk count cannot be negative.");
        var directoryOffset = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(16, 8));
        var directoryLength = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(24, 8));
        return new ContainerHeader(version, headerSize, chunkCount, directoryOffset, directoryLength);
    }

    private readonly record struct ContainerHeader(int FormatVersion, int HeaderSize, int ChunkCount, long DirectoryOffset, long DirectoryLength);
}

internal sealed record ContainerDirectoryDto
{
    public ContainerChunkDirectoryEntry[] Chunks { get; init; } = [];
}

internal sealed record ContainerChunkDirectoryEntry
{
    public string Id { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public int Version { get; init; }
    public long Offset { get; init; }
    public long Length { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }

    public ContainerChunkDirectoryEntry()
    {
    }

    public ContainerChunkDirectoryEntry(LlmContextContainerChunkInfo info, long offset, long length)
    {
        Id = info.Id;
        Kind = info.Kind;
        Format = info.Format;
        Version = info.Version;
        Offset = offset;
        Length = length;
        CreatedUtc = info.CreatedUtc;
        UpdatedUtc = info.UpdatedUtc;
    }
}
