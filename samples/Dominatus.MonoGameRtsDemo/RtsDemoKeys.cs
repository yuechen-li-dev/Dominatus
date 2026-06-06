using Dominatus.Core.Blackboard;
using Dominatus.Core.Runtime;

namespace Dominatus.MonoGameRtsDemo;

public static class RtsDemoKeys
{
    public static readonly BbKey<int> ShipIndex = new("rts_demo.ship_index");
    public static readonly BbKey<string> Faction = new("rts_demo.faction");
    public static readonly BbKey<string> CurrentAction = new("rts_demo.current_action");
    public static readonly BbKey<float> HullFraction = new("rts_demo.hull_fraction");
    public static readonly BbKey<float> NearestEnemyDistance = new("rts_demo.nearest_enemy_distance");
    public static readonly BbKey<bool> EnemyInRange = new("rts_demo.enemy_in_range");
    public static readonly BbKey<bool> CooldownReady = new("rts_demo.cooldown_ready");
    public static readonly BbKey<AgentId> TargetId = new("rts_demo.target_id");
    public static readonly BbKey<bool> UsedAiDecide = new("rts_demo.used_ai_decide");
}
