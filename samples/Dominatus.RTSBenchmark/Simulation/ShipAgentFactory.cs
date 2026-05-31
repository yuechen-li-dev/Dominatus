using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Dominatus.RTSBenchmark.Simulation;

public static class ShipAgentFactory
{
    private static readonly string[] ActionIds = Enum.GetNames<ShipActionType>();

    public static int UtilityOptionCount => ActionIds.Length;

    public static AiAgent Create(ShipState ship)
    {
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = DecideNode });
        foreach (var action in ActionIds)
        {
            var local = action;
            graph.Add(new HfsmStateDef { Id = local, Node = ctx => ActionNode(ctx, local) });
        }

        var agent = new AiAgent(new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true }));
        var def = ShipClassDefinition.Get(ship.Class);
        agent.Bb.Set(BenchmarkBlackboardKeys.ShipId, ship.Id);
        agent.Bb.Set(BenchmarkBlackboardKeys.IsRepairShip, def.RepairAmount > 0);
        agent.Bb.Set(BenchmarkBlackboardKeys.IsCarrier, ship.Class is ShipClass.Carrier or ShipClass.HiveArk);
        agent.Bb.Set(BenchmarkBlackboardKeys.IsCollective, ship.Faction == Faction.Collective);
        agent.Bb.Set(BenchmarkBlackboardKeys.IsDrone, ship.Class == ShipClass.NeedleDrone);
        agent.Bb.Set(BenchmarkBlackboardKeys.IsCommander, def.CommandRadius > 0);
        agent.Bb.Set(BenchmarkBlackboardKeys.CurrentAction, ShipActionType.Idle.ToString());
        return agent;
    }

    private static IEnumerator<AiStep> DecideNode(AiCtx ctx)
    {
        while (true)
        {
            var shipId = ctx.Bb.GetOrDefault(BenchmarkBlackboardKeys.ShipId, 0);
            yield return Ai.Decide(new DecisionSlot($"RTS.Ship.{shipId}.Action"),
            [
                Ai.Option(nameof(ShipActionType.Advance), UtilityScorers.Advance, nameof(ShipActionType.Advance)),
                Ai.Option(nameof(ShipActionType.FocusFire), UtilityScorers.FocusFire, nameof(ShipActionType.FocusFire)),
                Ai.Option(nameof(ShipActionType.Retreat), UtilityScorers.Retreat, nameof(ShipActionType.Retreat)),
                Ai.Option(nameof(ShipActionType.RepairAlly), UtilityScorers.RepairAlly, nameof(ShipActionType.RepairAlly)),
                Ai.Option(nameof(ShipActionType.ScreenHighValue), UtilityScorers.ScreenHighValue, nameof(ShipActionType.ScreenHighValue)),
                Ai.Option(nameof(ShipActionType.LaunchDrone), UtilityScorers.LaunchDrone, nameof(ShipActionType.LaunchDrone)),
                Ai.Option(nameof(ShipActionType.Regenerate), UtilityScorers.Regenerate, nameof(ShipActionType.Regenerate)),
                Ai.Option(nameof(ShipActionType.HoldFormation), UtilityScorers.HoldFormation, nameof(ShipActionType.HoldFormation)),
                Ai.Option(nameof(ShipActionType.Idle), UtilityScorers.Idle, nameof(ShipActionType.Idle))
            ], hysteresis: 0f, minCommitSeconds: 0f, tieEpsilon: 0.00001f);
        }
    }

    private static IEnumerator<AiStep> ActionNode(AiCtx ctx, string action)
    {
        while (true)
        {
            ctx.Bb.Set(BenchmarkBlackboardKeys.CurrentAction, action);
            yield return Ai.Steady(action);
        }
    }
}
