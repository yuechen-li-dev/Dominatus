using Dominatus.Core.Blackboard;
using Dominatus.GodotConn;
using Godot;

namespace Dominatus.GodotTinyTown;

public static class TinyTownKeys
{
    public static readonly BbKey<string> VillagerName = new("godot.tinytown.villager_name");
    public static readonly BbKey<string> PersonalityName = new("godot.tinytown.personality_name");
    public static readonly BbKey<string> SocialBuddyName = new("godot.tinytown.social_buddy_name");
    public static readonly BbKey<string> CurrentActivity = new("godot.tinytown.current_activity");
    public static readonly BbKey<string> CurrentNeed = new("godot.tinytown.current_need");
    public static readonly BbKey<string> CurrentIntent = new("godot.tinytown.current_intent");
    public static readonly BbKey<string> CurrentPhase = new("godot.tinytown.current_phase");
    public static readonly BbKey<string> CurrentTargetKind = new("godot.tinytown.current_target_kind");
    public static readonly BbKey<string> LastDecisionWinner = new("godot.tinytown.last_decision_winner");
    public static readonly BbKey<string> LastActivity = new("godot.tinytown.last_activity");
    public static readonly BbKey<float> LastDecisionScore = new("godot.tinytown.last_decision_score");
    public static readonly BbKey<Vector2> CurrentTargetPosition = GodotBbKeys.Vector2("godot.tinytown.current_target_position");
    public static readonly BbKey<float> Hunger = new("godot.tinytown.hunger");
    public static readonly BbKey<float> Thirst = new("godot.tinytown.thirst");
    public static readonly BbKey<float> RestNeed = new("godot.tinytown.rest_need");
    public static readonly BbKey<float> JoyNeed = new("godot.tinytown.joy_need");
    public static readonly BbKey<float> SocialNeed = new("godot.tinytown.social_need");
    public static readonly BbKey<float> NextNeedTickAt = new("godot.tinytown.next_need_tick_at");
    public static readonly BbKey<float> ActivityRemainingSeconds = new("godot.tinytown.activity_remaining_seconds");
    public static readonly BbKey<float> WellCooldownSeconds = new("godot.tinytown.well_cooldown_seconds");
    public static readonly BbKey<float> MarketCooldownSeconds = new("godot.tinytown.market_cooldown_seconds");
    public static readonly BbKey<float> GardenCooldownSeconds = new("godot.tinytown.garden_cooldown_seconds");
    public static readonly BbKey<float> RestCooldownSeconds = new("godot.tinytown.rest_cooldown_seconds");
    public static readonly BbKey<float> SocialCooldownSeconds = new("godot.tinytown.social_cooldown_seconds");
    public static readonly BbKey<float> WanderCooldownSeconds = new("godot.tinytown.wander_cooldown_seconds");
    public static readonly BbKey<float> ReturnHomeCooldownSeconds = new("godot.tinytown.return_home_cooldown_seconds");
    public static readonly BbKey<int> ActivityCycleIndex = new("godot.tinytown.activity_cycle_index");
    public static readonly BbKey<int> WanderIndex = new("godot.tinytown.wander_index");
    public static readonly BbKey<Vector2> InitialPosition = GodotBbKeys.Vector2("godot.tinytown.initial_position");
    public static readonly BbKey<Vector2> HomePosition = GodotBbKeys.Vector2("godot.tinytown.home_position");
    public static readonly BbKey<Vector2> WellPosition = GodotBbKeys.Vector2("godot.tinytown.well_position");
    public static readonly BbKey<Vector2> MarketPosition = GodotBbKeys.Vector2("godot.tinytown.market_position");
    public static readonly BbKey<Vector2> GardenPosition = GodotBbKeys.Vector2("godot.tinytown.garden_position");
}
