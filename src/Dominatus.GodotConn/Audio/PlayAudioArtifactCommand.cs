using Dominatus.Core.Runtime;

namespace Dominatus.GodotConn.Audio;

public sealed record PlayAudioArtifactCommand : IActuationCommand
{
    public required string ArtifactPath { get; init; }
    public string? MetadataPath { get; init; }
    public float VolumeDb { get; init; }
    public bool StopCurrent { get; init; } = true;
    public string? DebugLabel { get; init; }
}

public sealed record PlayAudioArtifactResult
{
    public required string ArtifactPath { get; init; }
    public string? MetadataPath { get; init; }
    public required string ResolvedPath { get; init; }
    public required string StreamKind { get; init; }
    public string? DebugLabel { get; init; }
}

public readonly record struct AudioPlaybackStateSnapshot(
    string? LastArtifactPath,
    string? LastMetadataPath,
    string? LastDebugLabel,
    bool IsPlaying,
    bool ObservedPlayback,
    int PlayedCount,
    int PlaybackFailureCount,
    float VolumeDb,
    string? LastError);
