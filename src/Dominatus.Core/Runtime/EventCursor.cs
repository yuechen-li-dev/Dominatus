namespace Dominatus.Core.Runtime;

/// <summary>
/// Cursor into a per-type event bucket. Stored by the waiter (NodeRunner).
/// </summary>
public struct EventCursor
{
    public int Index;
}