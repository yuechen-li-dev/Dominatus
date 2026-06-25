using Dominatus.Core.Blackboard;
using Godot;

namespace Dominatus.GodotConn;

public static class GodotBbKeys
{
    public static BbKey<Vector2> Vector2(string name) => new(name);

    public static BbKey<Vector3> Vector3(string name) => new(name);

    public static BbKey<NodePath> NodePath(string name) => new(name);
}
