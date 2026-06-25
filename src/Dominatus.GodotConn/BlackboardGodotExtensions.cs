using Dominatus.Core.Blackboard;
using Godot;
using System.Diagnostics.CodeAnalysis;

namespace Dominatus.GodotConn;

public static class BlackboardGodotExtensions
{
    public static bool TryResolveNode<TNode>(
        this Blackboard blackboard,
        BbKey<NodePath> key,
        Node owner,
        [NotNullWhen(true)] out TNode? node)
        where TNode : Node
    {
        ArgumentNullException.ThrowIfNull(blackboard);
        ArgumentNullException.ThrowIfNull(owner);

        if (blackboard.TryGet(key, out var path) && !string.IsNullOrEmpty(path.ToString()))
        {
            node = owner.GetNodeOrNull<TNode>(path);
            return node is not null;
        }

        node = null;
        return false;
    }
}
