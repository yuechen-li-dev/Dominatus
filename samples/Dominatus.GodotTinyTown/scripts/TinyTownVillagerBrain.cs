using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Dominatus.GodotConn;
using Dominatus.GodotConn.Actuation;
using Dominatus.OptFlow;
using Godot;

namespace Dominatus.GodotTinyTown;

public partial class TinyTownVillagerBrain : DominatusAgentNode
{
    private static readonly StateId RootState = StateId.Of("Root");
    private static readonly StateId WanderState = StateId.Of("Wander");
    private static readonly StateId GoToWellState = StateId.Of("GoToWell");
    private static readonly StateId GoToMarketState = StateId.Of("GoToMarket");
    private static readonly StateId RestAtHomeState = StateId.Of("RestAtHome");
    private static readonly StateId TendGardenState = StateId.Of("TendGarden");

    [Export]
    public string VillagerName { get; set; } = "Villager";

    [Export]
    public NodePath HomeMarkerPath { get; set; } = new();

    [Export]
    public NodePath WellMarkerPath { get; set; } = new();

    [Export]
    public NodePath MarketMarkerPath { get; set; } = new();

    [Export]
    public NodePath GardenMarkerPath { get; set; } = new();

    [Export]
    public float MoveSpeed { get; set; } = 82f;

    [Export]
    public float ArrivalDistance { get; set; } = 10f;

    [Export]
    public float NeedTickSeconds { get; set; } = 0.35f;

    [Export]
    public float DecisionHysteresis { get; set; } = 0.05f;

    public override void _Ready()
    {
        if (GetNodeOrNull<TinyTownWorld>("../../../DominatusWorld") is { } tinyTownWorld)
            AttachToWorld(tinyTownWorld);

        base._Ready();

        if (WorldNode is TinyTownWorld world && GetParent() is CharacterBody2D body)
            world.RegisterVillager(this, body);
    }

    public override void _ExitTree()
    {
        if (GetNodeOrNull<TinyTownWorld>("../../../DominatusWorld") is { } tinyTownWorld
            && Agent is not null)
            tinyTownWorld.UnregisterVillager(this);

        base._ExitTree();
    }

    protected override HfsmGraph ConfigureGraph()
    {
        var graph = new HfsmGraph { Root = RootState };
        graph.Add(RootState, DecideNode);
        graph.Add(WanderState, WanderNode);
        graph.Add(GoToWellState, GoToWellNode);
        graph.Add(GoToMarketState, GoToMarketNode);
        graph.Add(RestAtHomeState, RestAtHomeNode);
        graph.Add(TendGardenState, TendGardenNode);
        return graph;
    }

    protected override HfsmOptions CreateHfsmOptions() => new()
    {
        KeepRootFrame = true
    };

    protected override void ConfigureBlackboard(Blackboard blackboard)
    {
        blackboard.Set(TinyTownKeys.VillagerName, VillagerName);
        blackboard.Set(TinyTownKeys.CurrentActivity, "Arriving");
        blackboard.Set(TinyTownKeys.CurrentNeed, "Settling in");
        blackboard.Set(TinyTownKeys.Hunger, 0.82f);
        blackboard.Set(TinyTownKeys.Thirst, 0.78f);
        blackboard.Set(TinyTownKeys.Energy, 0.85f);
        blackboard.Set(TinyTownKeys.GardenJoy, 0.74f);
        blackboard.Set(TinyTownKeys.WanderIndex, Mathf.Abs(VillagerName.GetHashCode()) % 3);
        blackboard.Set(TinyTownKeys.NextNeedDecayAt, 0f);
        blackboard.Set(TinyTownKeys.HomePosition, ResolveMarker(HomeMarkerPath).GlobalPosition);
        blackboard.Set(TinyTownKeys.WellPosition, ResolveMarker(WellMarkerPath).GlobalPosition);
        blackboard.Set(TinyTownKeys.MarketPosition, ResolveMarker(MarketMarkerPath).GlobalPosition);
        blackboard.Set(TinyTownKeys.GardenPosition, ResolveMarker(GardenMarkerPath).GlobalPosition);
    }

    private IEnumerator<AiStep> DecideNode(AiCtx ctx)
    {
        while (true)
        {
            TickNeeds(ctx);
            UpdateCurrentNeed(ctx);

            yield return Ai.Decide(
            new DecisionSlot($"GodotTinyTown.{VillagerName}.NextActivity"),
            [
                Ai.Option("GoToWell", NeedUrgency(TinyTownKeys.Thirst), GoToWellState),
                Ai.Option("GoToMarket", NeedUrgency(TinyTownKeys.Hunger), GoToMarketState),
                Ai.Option("RestAtHome", NeedUrgency(TinyTownKeys.Energy), RestAtHomeState),
                Ai.Option("TendGarden", NeedUrgency(TinyTownKeys.GardenJoy), TendGardenState),
                Ai.Option("Wander", Consideration.Constant(0.34f), WanderState)
            ],
            hysteresis: DecisionHysteresis,
            minCommitSeconds: 0.65f,
            tieEpsilon: 0.0001f);
        }
    }

    private IEnumerator<AiStep> WanderNode(AiCtx ctx)
    {
        while (true)
        {
            var target = SelectWanderTarget(ctx);
            ctx.Bb.Set(TinyTownKeys.CurrentActivity, "Wander");
            ctx.Bb.Set(TinyTownKeys.CurrentTargetPosition, target);

            if (TryArrive(ctx, target))
            {
                ctx.Bb.Set(TinyTownKeys.WanderIndex, ctx.Bb.GetOrDefault(TinyTownKeys.WanderIndex, 0) + 1);
                yield return Ai.Act(new Move2DCommand(Vector2.Zero));
                yield return Ai.Wait(0.55f);
                continue;
            }

            yield return Ai.Act(new Move2DCommand(CreateVelocityTo(target)));
            yield return Ai.Wait(0.05f);
        }
    }

    private IEnumerator<AiStep> GoToWellNode(AiCtx ctx)
    {
        while (true)
        {
            var target = ctx.Bb.GetOrDefault(TinyTownKeys.WellPosition, Vector2.Zero);
            ctx.Bb.Set(TinyTownKeys.CurrentActivity, "GoToWell");
            ctx.Bb.Set(TinyTownKeys.CurrentTargetPosition, target);

            if (TryArrive(ctx, target))
            {
                ctx.Bb.Set(TinyTownKeys.Thirst, 1f);
                ctx.Bb.Set(TinyTownKeys.CurrentNeed, "Satisfied");
                yield return Ai.Act(new Move2DCommand(Vector2.Zero));
                yield return Ai.Wait(0.7f);
                continue;
            }

            yield return Ai.Act(new Move2DCommand(CreateVelocityTo(target)));
            yield return Ai.Wait(0.05f);
        }
    }

    private IEnumerator<AiStep> GoToMarketNode(AiCtx ctx)
    {
        while (true)
        {
            var target = ctx.Bb.GetOrDefault(TinyTownKeys.MarketPosition, Vector2.Zero);
            ctx.Bb.Set(TinyTownKeys.CurrentActivity, "GoToMarket");
            ctx.Bb.Set(TinyTownKeys.CurrentTargetPosition, target);

            if (TryArrive(ctx, target))
            {
                ctx.Bb.Set(TinyTownKeys.Hunger, 1f);
                ctx.Bb.Set(TinyTownKeys.CurrentNeed, "Satisfied");
                yield return Ai.Act(new Move2DCommand(Vector2.Zero));
                yield return Ai.Wait(0.75f);
                continue;
            }

            yield return Ai.Act(new Move2DCommand(CreateVelocityTo(target)));
            yield return Ai.Wait(0.05f);
        }
    }

    private IEnumerator<AiStep> RestAtHomeNode(AiCtx ctx)
    {
        while (true)
        {
            var target = ctx.Bb.GetOrDefault(TinyTownKeys.HomePosition, Vector2.Zero);
            ctx.Bb.Set(TinyTownKeys.CurrentActivity, "RestAtHome");
            ctx.Bb.Set(TinyTownKeys.CurrentTargetPosition, target);

            if (TryArrive(ctx, target))
            {
                ctx.Bb.Set(TinyTownKeys.Energy, Clamp01(ctx.Bb.GetOrDefault(TinyTownKeys.Energy, 1f) + 0.18f));
                ctx.Bb.Set(TinyTownKeys.CurrentNeed, "Resting");
                yield return Ai.Act(new Move2DCommand(Vector2.Zero));
                yield return Ai.Wait(0.85f);
                continue;
            }

            yield return Ai.Act(new Move2DCommand(CreateVelocityTo(target)));
            yield return Ai.Wait(0.05f);
        }
    }

    private IEnumerator<AiStep> TendGardenNode(AiCtx ctx)
    {
        while (true)
        {
            var target = ctx.Bb.GetOrDefault(TinyTownKeys.GardenPosition, Vector2.Zero);
            ctx.Bb.Set(TinyTownKeys.CurrentActivity, "TendGarden");
            ctx.Bb.Set(TinyTownKeys.CurrentTargetPosition, target);

            if (TryArrive(ctx, target))
            {
                ctx.Bb.Set(TinyTownKeys.GardenJoy, 1f);
                ctx.Bb.Set(TinyTownKeys.CurrentNeed, "Content");
                yield return Ai.Act(new Move2DCommand(Vector2.Zero));
                yield return Ai.Wait(0.8f);
                continue;
            }

            yield return Ai.Act(new Move2DCommand(CreateVelocityTo(target)));
            yield return Ai.Wait(0.05f);
        }
    }

    private static Consideration NeedUrgency(BbKey<float> key)
        => new((_, agent) => 1f - agent.Bb.GetOrDefault(key, 1f));

    private void TickNeeds(AiCtx ctx)
    {
        var now = ctx.World.Clock.Time;
        var nextTickAt = ctx.Bb.GetOrDefault(TinyTownKeys.NextNeedDecayAt, 0f);
        if (now < nextTickAt)
            return;

        ctx.Bb.Set(TinyTownKeys.NextNeedDecayAt, now + NeedTickSeconds);
        Decay(ctx, TinyTownKeys.Hunger, 0.035f);
        Decay(ctx, TinyTownKeys.Thirst, 0.045f);
        Decay(ctx, TinyTownKeys.Energy, 0.025f);
        Decay(ctx, TinyTownKeys.GardenJoy, 0.02f);
    }

    private void UpdateCurrentNeed(AiCtx ctx)
    {
        var thirstUrgency = 1f - ctx.Bb.GetOrDefault(TinyTownKeys.Thirst, 1f);
        var hungerUrgency = 1f - ctx.Bb.GetOrDefault(TinyTownKeys.Hunger, 1f);
        var energyUrgency = 1f - ctx.Bb.GetOrDefault(TinyTownKeys.Energy, 1f);
        var joyUrgency = 1f - ctx.Bb.GetOrDefault(TinyTownKeys.GardenJoy, 1f);

        var max = thirstUrgency;
        var label = "Thirst";

        if (hungerUrgency > max)
        {
            max = hungerUrgency;
            label = "Hunger";
        }

        if (energyUrgency > max)
        {
            max = energyUrgency;
            label = "Energy";
        }

        if (joyUrgency > max)
            label = "Garden Joy";

        ctx.Bb.Set(TinyTownKeys.CurrentNeed, label);
    }

    private Vector2 SelectWanderTarget(AiCtx ctx)
    {
        return (ctx.Bb.GetOrDefault(TinyTownKeys.WanderIndex, 0) % 3) switch
        {
            0 => ctx.Bb.GetOrDefault(TinyTownKeys.MarketPosition, Vector2.Zero),
            1 => ctx.Bb.GetOrDefault(TinyTownKeys.GardenPosition, Vector2.Zero),
            _ => ctx.Bb.GetOrDefault(TinyTownKeys.WellPosition, Vector2.Zero)
        };
    }

    private bool TryArrive(AiCtx ctx, Vector2 target)
    {
        var distance = GetBody().GlobalPosition.DistanceTo(target);
        return distance <= ArrivalDistance;
    }

    private Vector2 CreateVelocityTo(Vector2 target)
    {
        var from = GetBody().GlobalPosition;
        var delta = target - from;
        return delta.LengthSquared() <= 0.001f
            ? Vector2.Zero
            : delta.Normalized() * MoveSpeed;
    }

    private CharacterBody2D GetBody()
        => GetParent() as CharacterBody2D
            ?? throw new InvalidOperationException("TinyTownVillagerBrain expects to be attached under a CharacterBody2D.");

    private Marker2D ResolveMarker(NodePath path)
        => GetNode<Marker2D>(path);

    private static void Decay(AiCtx ctx, BbKey<float> key, float amount)
        => ctx.Bb.Set(key, Clamp01(ctx.Bb.GetOrDefault(key, 1f) - amount));

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
}
