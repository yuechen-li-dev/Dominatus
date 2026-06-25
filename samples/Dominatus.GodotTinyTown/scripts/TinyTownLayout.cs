using Godot;

namespace Dominatus.GodotTinyTown;

public static class TinyTownLayout
{
    public static readonly Vector2I ViewportSize = new(1152, 648);
    public static readonly Rect2 TownRect = new(24f, 24f, 792f, 600f);
    public static readonly Rect2 DebugPanelRect = new(840f, 24f, 288f, 600f);
    public static readonly Vector2 DestinationMarkerSize = new(52f, 38f);
    public static readonly Vector2 VillagerVisualSize = new(22f, 22f);
    public static readonly Vector2 VillagerLabelPadding = new(10f, 8f);
    public static readonly Vector2 DestinationLabelPadding = new(10f, 6f);
    public const float VillagerStatusBottomOffset = -44f;
    public const float VillagerStatusBottomOffsetNearBottom = -54f;
    public const float DestinationLabelTopOffset = 34f;
    public const float DestinationLabelMinWidth = 60f;

    public static Vector2 ComputeVillagerPlatePosition(Vector2 plateSize, Vector2 globalPosition)
    {
        var x = -plateSize.X * 0.5f;
        var y = globalPosition.Y > 470f
            ? VillagerStatusBottomOffsetNearBottom - plateSize.Y
            : VillagerStatusBottomOffset - plateSize.Y;

        if (globalPosition.X < 250f)
            x += 36f;
        else if (globalPosition.X > TownRect.Position.X + TownRect.Size.X - 120f)
            x -= 28f;

        return new Vector2(x, y);
    }

    public static Vector2 ComputeDestinationLabelPosition(Vector2 plateSize, Vector2 globalPosition)
    {
        var x = -plateSize.X * 0.5f;
        if (globalPosition.X < 170f)
            x += 12f;
        else if (globalPosition.X > TownRect.Position.X + TownRect.Size.X - 100f)
            x -= 12f;

        return new Vector2(x, DestinationLabelTopOffset);
    }
}
