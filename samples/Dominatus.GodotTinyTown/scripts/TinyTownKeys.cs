using Dominatus.Core.Blackboard;
using Dominatus.GodotConn;
using Godot;

namespace Dominatus.GodotTinyTown;

public static class TinyTownKeys
{
    public static readonly BbKey<string> VillagerName = new("godot.tinytown.villager_name");
    public static readonly BbKey<string> CurrentActivity = new("godot.tinytown.current_activity");
    public static readonly BbKey<string> CurrentNeed = new("godot.tinytown.current_need");
    public static readonly BbKey<Vector2> CurrentTargetPosition = GodotBbKeys.Vector2("godot.tinytown.current_target_position");
    public static readonly BbKey<float> Hunger = new("godot.tinytown.hunger");
    public static readonly BbKey<float> Thirst = new("godot.tinytown.thirst");
    public static readonly BbKey<float> Energy = new("godot.tinytown.energy");
    public static readonly BbKey<float> GardenJoy = new("godot.tinytown.garden_joy");
    public static readonly BbKey<float> NextNeedDecayAt = new("godot.tinytown.next_need_decay_at");
    public static readonly BbKey<int> WanderIndex = new("godot.tinytown.wander_index");
    public static readonly BbKey<Vector2> HomePosition = GodotBbKeys.Vector2("godot.tinytown.home_position");
    public static readonly BbKey<Vector2> WellPosition = GodotBbKeys.Vector2("godot.tinytown.well_position");
    public static readonly BbKey<Vector2> MarketPosition = GodotBbKeys.Vector2("godot.tinytown.market_position");
    public static readonly BbKey<Vector2> GardenPosition = GodotBbKeys.Vector2("godot.tinytown.garden_position");
}
