using Godot;

namespace Dominatus.GodotTinyTown;

public static class TinyTownLayout
{
    public static readonly Vector2I ViewportSize = new(1152, 648);
    public static readonly Rect2 TownRect = new(24f, 24f, 792f, 600f);
    public static readonly Rect2 DebugPanelRect = new(840f, 24f, 288f, 600f);
    public static readonly Vector2 DestinationMarkerSize = new(52f, 38f);
    public static readonly Vector2 VillagerVisualSize = new(22f, 22f);
    public static readonly Vector2 VillagerLabelOffset = new(-52f, -86f);
    public static readonly Vector2 VillagerLabelPadding = new(10f, 8f);
}
