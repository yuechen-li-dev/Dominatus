using Godot;

namespace Dominatus.GodotConn.Audio;

public static class GodotAudioArtifactLoader
{
    public static AudioStream LoadRequired(string artifactPath)
    {
        if (!TryLoad(artifactPath, out var stream, out _, out var error))
            throw new InvalidOperationException(error);

        return stream!;
    }

    public static bool TryLoad(
        string artifactPath,
        out AudioStream? stream,
        out string? resolvedPath,
        out string? error)
    {
        stream = null;
        resolvedPath = null;
        error = null;

        if (string.IsNullOrWhiteSpace(artifactPath))
        {
            error = "Audio artifact path was empty.";
            return false;
        }

        resolvedPath = ResolveFilePath(artifactPath);
        if (!File.Exists(resolvedPath))
        {
            error = $"Audio artifact file was not found: {resolvedPath}";
            return false;
        }

        try
        {
            var extension = Path.GetExtension(resolvedPath).ToLowerInvariant();
            stream = extension switch
            {
                ".wav" => AudioStreamWav.LoadFromFile(resolvedPath, []),
                ".mp3" => AudioStreamMP3.LoadFromFile(resolvedPath),
                _ => null
            };

            if (stream is not null)
                return true;

            error = extension switch
            {
                ".ogg" => "Ogg playback is not currently supported by the Dominatus Godot audio bridge.",
                ".flac" => "Flac playback is not currently supported by the Dominatus Godot audio bridge.",
                _ => $"Unsupported audio artifact format '{extension}'."
            };
            return false;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            error = $"Failed to load audio artifact '{resolvedPath}': {ex.Message}";
            return false;
        }
    }

    public static string ResolveFilePath(string path)
    {
        if (path.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectSettings.GlobalizePath(path);
        }

        return Path.GetFullPath(path);
    }
}
