using System.Text.Json.Serialization;

namespace Dominatus.Core.Persistence;

/// <summary>
/// Log of nondeterministic inputs since a checkpoint.
/// Keep this schema stable (or versioned) to support rollback/replay.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ReplayEvent.Advance), "advance")]
[JsonDerivedType(typeof(ReplayEvent.Choice), "choice")]
[JsonDerivedType(typeof(ReplayEvent.Text), "text")]
[JsonDerivedType(typeof(ReplayEvent.External), "external")]
[JsonDerivedType(typeof(ReplayEvent.RngSeed), "rngSeed")]
public abstract record ReplayEvent
{
    public sealed record Advance(string AgentId) : ReplayEvent;

    public sealed record Choice(string AgentId, string ChoiceKey) : ReplayEvent;

    public sealed record Text(string AgentId, string Value) : ReplayEvent;

    /// <summary>
    /// Host-defined external input/event (e.g. "DoorOpened") recorded as a string tag + json payload.
    /// </summary>
    public sealed record External(string AgentId, string Type, string JsonPayload) : ReplayEvent;

    /// <summary>
    /// RNG determinism (optional): either store seed in checkpoint or log draws here.
    /// </summary>
    public sealed record RngSeed(int Seed) : ReplayEvent;
}
