using System.Buffers.Binary;

namespace Dominatus.SpriteForge;

public static class SpriteForgeImageInspector
{
    public static bool TryReadImageDimensions(string path, out int width, out int height, out string error)
    {
        width = 0;
        height = 0;
        error = string.Empty;

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension switch
            {
                ".png" => TryReadPngDimensions(reader, out width, out height, out error),
                _ => Fail($"Unsupported image extension '{extension}'. SpriteForge currently validates PNG atlases.", out width, out height, out error)
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryReadPngDimensions(BinaryReader reader, out int width, out int height, out string error)
    {
        width = 0;
        height = 0;
        error = string.Empty;

        Span<byte> signature = stackalloc byte[8];
        if (reader.Read(signature) != signature.Length
            || !signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            error = "File is not a valid PNG.";
            return false;
        }

        Span<byte> chunkLength = stackalloc byte[4];
        Span<byte> chunkType = stackalloc byte[4];
        if (reader.Read(chunkLength) != chunkLength.Length || reader.Read(chunkType) != chunkType.Length)
        {
            error = "PNG header is truncated.";
            return false;
        }

        if (!chunkType.SequenceEqual("IHDR"u8))
        {
            error = "PNG is missing the IHDR header chunk.";
            return false;
        }

        Span<byte> dimensions = stackalloc byte[8];
        if (reader.Read(dimensions) != dimensions.Length)
        {
            error = "PNG IHDR chunk is truncated.";
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(dimensions[..4]);
        height = BinaryPrimitives.ReadInt32BigEndian(dimensions[4..]);
        return width > 0 && height > 0
            ? true
            : Fail("PNG width and height must be greater than zero.", out width, out height, out error);
    }

    private static bool Fail(string message, out int width, out int height, out string error)
    {
        width = 0;
        height = 0;
        error = message;
        return false;
    }
}
