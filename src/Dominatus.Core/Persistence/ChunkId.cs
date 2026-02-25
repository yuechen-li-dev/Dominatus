namespace Dominatus.Core.Persistence;

/// <summary>
/// Small fixed identifiers for save chunks.
/// Keep stable forever once shipped.
/// </summary>
public readonly record struct ChunkId(string Value)
{
    public override string ToString() => Value;

    public static readonly ChunkId Hfsm = new("dom.hfsm");
    public static readonly ChunkId Blackboard = new("dom.bb");
    public static readonly ChunkId EventCursors = new("dom.evcur");
    public static readonly ChunkId ReplayLog = new("dom.replay");
    public static readonly ChunkId Meta = new("dom.meta");
}