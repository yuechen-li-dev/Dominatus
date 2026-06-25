using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.GodotConn;
using Dominatus.GodotConn.Actuation;
using Dominatus.OptFlow;
using Dominatus.UtilityLite;
using Godot;

namespace Dominatus.GodotTinyTown;

public partial class TinyTownVillagerBrain : DominatusAgentNode
{
    private static readonly StateId RootState = StateId.Of("Root");
    private static readonly StateId WanderState = StateId.Of("Wander");
    private static readonly StateId DrinkAtWellState = StateId.Of("DrinkAtWell");
    private static readonly StateId ShopAtMarketState = StateId.Of("ShopAtMarket");
    private static readonly StateId RestAtHomeState = StateId.Of("RestAtHome");
    private static readonly StateId TendGardenState = StateId.Of("TendGarden");
    private static readonly StateId SocializeState = StateId.Of("Socialize");
    private static readonly StateId ReturnHomeState = StateId.Of("ReturnHome");
    private static readonly DecisionPolicy ActivityPolicy = Utility.Policy(hysteresis: 0.08f, minCommitSeconds: 1.35f, tieEpsilon: 0.0001f);
    private static readonly Vector2[] WellOffsets =
    [
        new Vector2(-18f, -16f),
        new Vector2(18f, -16f),
        new Vector2(-18f, 16f),
        new Vector2(18f, 16f)
    ];
    private static readonly Vector2[] MarketOffsets =
    [
        new Vector2(-24f, -18f),
        new Vector2(24f, -18f),
        new Vector2(-24f, 18f),
        new Vector2(24f, 18f)
    ];
    private static readonly Vector2[] GardenOffsets =
    [
        new Vector2(-22f, -14f),
        new Vector2(22f, -14f),
        new Vector2(-22f, 14f),
        new Vector2(22f, 14f)
    ];
    private static readonly Vector2[] WanderPoints =
    [
        new Vector2(332f, 256f),
        new Vector2(450f, 308f),
        new Vector2(548f, 248f),
        new Vector2(418f, 396f),
        new Vector2(592f, 366f)
    ];

    private VillagerProfile? _profile;

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
        graph.Add(WanderState, ctx => ActivityNode(ctx, TinyTownIntent.Wander));
        graph.Add(DrinkAtWellState, ctx => ActivityNode(ctx, TinyTownIntent.DrinkAtWell));
        graph.Add(ShopAtMarketState, ctx => ActivityNode(ctx, TinyTownIntent.ShopAtMarket));
        graph.Add(RestAtHomeState, ctx => ActivityNode(ctx, TinyTownIntent.RestAtHome));
        graph.Add(TendGardenState, ctx => ActivityNode(ctx, TinyTownIntent.TendGarden));
        graph.Add(SocializeState, ctx => ActivityNode(ctx, TinyTownIntent.Socialize));
        graph.Add(ReturnHomeState, ctx => ActivityNode(ctx, TinyTownIntent.ReturnHome));
        return graph;
    }

    protected override HfsmOptions CreateHfsmOptions() => new()
    {
        KeepRootFrame = true
    };

    protected override void ConfigureBlackboard(Blackboard blackboard)
    {
        _profile = VillagerProfile.For(VillagerName);

        blackboard.Set(TinyTownKeys.VillagerName, VillagerName);
        blackboard.Set(TinyTownKeys.PersonalityName, _profile.PersonalityName);
        blackboard.Set(TinyTownKeys.SocialBuddyName, _profile.SocialBuddyName);
        blackboard.Set(TinyTownKeys.CurrentActivity, "Arriving");
        blackboard.Set(TinyTownKeys.CurrentNeed, "Settling in");
        blackboard.Set(TinyTownKeys.CurrentIntent, TinyTownIntent.ReturnHome.Id);
        blackboard.Set(TinyTownKeys.CurrentPhase, "Choose");
        blackboard.Set(TinyTownKeys.CurrentTargetKind, "Home");
        blackboard.Set(TinyTownKeys.LastDecisionWinner, string.Empty);
        blackboard.Set(TinyTownKeys.LastActivity, string.Empty);
        blackboard.Set(TinyTownKeys.LastDecisionScore, 0f);
        blackboard.Set(TinyTownKeys.Hunger, _profile.StartHungerNeed);
        blackboard.Set(TinyTownKeys.Thirst, _profile.StartThirstNeed);
        blackboard.Set(TinyTownKeys.RestNeed, _profile.StartRestNeed);
        blackboard.Set(TinyTownKeys.JoyNeed, _profile.StartJoyNeed);
        blackboard.Set(TinyTownKeys.SocialNeed, _profile.StartSocialNeed);
        blackboard.Set(TinyTownKeys.WanderIndex, _profile.Index);
        blackboard.Set(TinyTownKeys.ActivityCycleIndex, 0);
        blackboard.Set(TinyTownKeys.ActivityRemainingSeconds, 0f);
        blackboard.Set(TinyTownKeys.NextNeedTickAt, 0f);
        blackboard.Set(TinyTownKeys.WellCooldownSeconds, 0f);
        blackboard.Set(TinyTownKeys.MarketCooldownSeconds, 0f);
        blackboard.Set(TinyTownKeys.GardenCooldownSeconds, 0f);
        blackboard.Set(TinyTownKeys.RestCooldownSeconds, 0f);
        blackboard.Set(TinyTownKeys.SocialCooldownSeconds, 0f);
        blackboard.Set(TinyTownKeys.WanderCooldownSeconds, 0f);
        blackboard.Set(TinyTownKeys.ReturnHomeCooldownSeconds, 0f);
        blackboard.Set(TinyTownKeys.InitialPosition, GetBody().GlobalPosition);
        blackboard.Set(TinyTownKeys.HomePosition, ResolveMarker(HomeMarkerPath).GlobalPosition);
        blackboard.Set(TinyTownKeys.WellPosition, ResolveMarker(WellMarkerPath).GlobalPosition);
        blackboard.Set(TinyTownKeys.MarketPosition, ResolveMarker(MarketMarkerPath).GlobalPosition);
        blackboard.Set(TinyTownKeys.GardenPosition, ResolveMarker(GardenMarkerPath).GlobalPosition);
    }

    private IEnumerator<AiStep> DecideNode(AiCtx ctx)
    {
        while (true)
        {
            TickSimulation(ctx);
            UpdateCurrentNeed(ctx);
            var options = BuildOptions(ctx);
            RecordDecisionSurface(ctx, options);

            yield return Ai.Decide(
                Utility.Slot($"GodotTinyTown.{VillagerName}.NextActivity"),
                options,
                hysteresis: MathF.Max(DecisionHysteresis, ActivityPolicy.Hysteresis),
                minCommitSeconds: ActivityPolicy.MinCommitSeconds,
                tieEpsilon: ActivityPolicy.TieEpsilon);
        }
    }

    private IEnumerator<AiStep> ActivityNode(AiCtx ctx, TinyTownIntent intent)
    {
        while (true)
        {
            var target = ResolveIntentTarget(ctx, intent);
            ctx.Bb.Set(TinyTownKeys.CurrentIntent, intent.Id);
            ctx.Bb.Set(TinyTownKeys.CurrentTargetPosition, target);
            ctx.Bb.Set(TinyTownKeys.CurrentTargetKind, intent.TargetKind);

            if (!TryArrive(target))
            {
                EnterTravelPhase(ctx, intent);
                yield return Ai.Act(new Move2DCommand(CreateVelocityTo(target)));
                yield return Ai.Wait(0.05f);
                continue;
            }

            if (ctx.Bb.GetOrDefault(TinyTownKeys.CurrentPhase, "Choose") != "Dwell")
            {
                ctx.Bb.Set(TinyTownKeys.ActivityRemainingSeconds, ComputeDwellSeconds(intent));
            }

            EnterDwellPhase(ctx, intent);
            yield return Ai.Act(new Move2DCommand(Vector2.Zero));
            yield return Ai.Wait(0.05f);
        }
    }

    private IReadOnlyList<UtilityOption> BuildOptions(AiCtx ctx)
    {
        var profile = Profile;
        var thirstNeed = Utility.Pow(Utility.Remap(Utility.Bb(TinyTownKeys.Thirst), 0.15f, 1f), 1.20f);
        var hungerNeed = Utility.Pow(Utility.Remap(Utility.Bb(TinyTownKeys.Hunger), 0.15f, 1f), 1.15f);
        var restNeed = Utility.Pow(Utility.Remap(Utility.Bb(TinyTownKeys.RestNeed), 0.10f, 1f), 1.25f);
        var joyNeed = Utility.Pow(Utility.Remap(Utility.Bb(TinyTownKeys.JoyNeed), 0.20f, 1f), 1.10f);
        var socialNeed = Utility.Pow(Utility.Remap(Utility.Bb(TinyTownKeys.SocialNeed), 0.15f, 1f), 1.12f);
        var calmNeeds = Utility.Not(Blend(
            (Utility.Bb(TinyTownKeys.Hunger), 0.24f),
            (Utility.Bb(TinyTownKeys.Thirst), 0.24f),
            (Utility.Bb(TinyTownKeys.RestNeed), 0.24f),
            (Utility.Bb(TinyTownKeys.JoyNeed), 0.14f),
            (Utility.Bb(TinyTownKeys.SocialNeed), 0.14f)));

        var drinkScore = CommitAware(TinyTownIntent.DrinkAtWell, Utility.Any(
            Utility.All(Utility.Threshold(Utility.Bb(TinyTownKeys.Thirst), 0.92f), Constant(0.99f)),
            Blend(
                (thirstNeed, 0.58f),
                (Constant(profile.WellBias), 0.12f),
                (TargetProximity(() => ResolveDestinationOffset(TinyTownKeys.WellPosition, WellOffsets)), 0.10f),
                (CooldownReady(TinyTownKeys.WellCooldownSeconds, 6f), 0.20f))));

        var marketScore = CommitAware(TinyTownIntent.ShopAtMarket, Blend(
            (hungerNeed, 0.48f),
            (Constant(profile.MarketBias), 0.20f),
            (Constant(profile.SocialBias), 0.08f),
            (TargetProximity(() => ResolveDestinationOffset(TinyTownKeys.MarketPosition, MarketOffsets)), 0.08f),
            (CooldownReady(TinyTownKeys.MarketCooldownSeconds, 6f), 0.16f)));

        var restScore = CommitAware(TinyTownIntent.RestAtHome, Utility.Any(
            Utility.All(Utility.Threshold(Utility.Bb(TinyTownKeys.RestNeed), 0.90f), Constant(0.99f)),
            Blend(
                (restNeed, 0.58f),
                (Constant(profile.RestBias), 0.16f),
                (Constant(profile.HomeBias), 0.12f),
                (CooldownReady(TinyTownKeys.RestCooldownSeconds, 7f), 0.14f))));

        var gardenScore = CommitAware(TinyTownIntent.TendGarden, Blend(
            (joyNeed, 0.42f),
            (Constant(profile.GardenBias), 0.24f),
            (ModerateNeed(TinyTownKeys.Hunger, 0.20f, 0.65f), 0.10f),
            (TargetProximity(() => ResolveDestinationOffset(TinyTownKeys.GardenPosition, GardenOffsets)), 0.08f),
            (CooldownReady(TinyTownKeys.GardenCooldownSeconds, 6f), 0.16f)));

        var wanderScore = CommitAware(TinyTownIntent.Wander, Blend(
            (calmNeeds, 0.46f),
            (Constant(profile.WanderBias), 0.32f),
            (CooldownReady(TinyTownKeys.WanderCooldownSeconds, 4f), 0.22f)));

        var socializeScore = CommitAware(TinyTownIntent.Socialize, Blend(
            (socialNeed, 0.48f),
            (Constant(profile.SocialBias), 0.18f),
            (SocialBuddyAvailable(), 0.18f),
            (CooldownReady(TinyTownKeys.SocialCooldownSeconds, 6f), 0.16f)));

        var returnHomeScore = CommitAware(TinyTownIntent.ReturnHome, Blend(
            (DistanceFromHome(), 0.46f),
            (calmNeeds, 0.18f),
            (Constant(profile.HomeBias), 0.22f),
            (CooldownReady(TinyTownKeys.ReturnHomeCooldownSeconds, 5f), 0.14f)));

        return
        [
            Utility.Option(TinyTownIntent.DrinkAtWell.Id, drinkScore, DrinkAtWellState),
            Utility.Option(TinyTownIntent.ShopAtMarket.Id, marketScore, ShopAtMarketState),
            Utility.Option(TinyTownIntent.RestAtHome.Id, restScore, RestAtHomeState),
            Utility.Option(TinyTownIntent.TendGarden.Id, gardenScore, TendGardenState),
            Utility.Option(TinyTownIntent.Wander.Id, wanderScore, WanderState),
            Utility.Option(TinyTownIntent.Socialize.Id, socializeScore, SocializeState),
            Utility.Option(TinyTownIntent.ReturnHome.Id, returnHomeScore, ReturnHomeState)
        ];
    }

    private void TickSimulation(AiCtx ctx)
    {
        var now = ctx.World.Clock.Time;
        var nextTickAt = ctx.Bb.GetOrDefault(TinyTownKeys.NextNeedTickAt, 0f);
        while (now >= nextTickAt)
        {
            TickOneNeedStep(ctx);
            nextTickAt = nextTickAt <= 0f ? now + NeedTickSeconds : nextTickAt + NeedTickSeconds;
        }

        ctx.Bb.Set(TinyTownKeys.NextNeedTickAt, nextTickAt);
    }

    private void TickOneNeedStep(AiCtx ctx)
    {
        var profile = Profile;
        IncreaseNeed(ctx, TinyTownKeys.Hunger, profile.HungerStep);
        IncreaseNeed(ctx, TinyTownKeys.Thirst, profile.ThirstStep);
        IncreaseNeed(ctx, TinyTownKeys.RestNeed, profile.RestStep);
        IncreaseNeed(ctx, TinyTownKeys.JoyNeed, profile.JoyStep);
        IncreaseNeed(ctx, TinyTownKeys.SocialNeed, profile.SocialStep);
        ReduceCooldown(ctx, TinyTownKeys.WellCooldownSeconds);
        ReduceCooldown(ctx, TinyTownKeys.MarketCooldownSeconds);
        ReduceCooldown(ctx, TinyTownKeys.GardenCooldownSeconds);
        ReduceCooldown(ctx, TinyTownKeys.RestCooldownSeconds);
        ReduceCooldown(ctx, TinyTownKeys.SocialCooldownSeconds);
        ReduceCooldown(ctx, TinyTownKeys.WanderCooldownSeconds);
        ReduceCooldown(ctx, TinyTownKeys.ReturnHomeCooldownSeconds);

        if (ctx.Bb.GetOrDefault(TinyTownKeys.CurrentPhase, "Choose") == "Dwell")
        {
            ApplyActivityEffects(ctx, ctx.Bb.GetOrDefault(TinyTownKeys.CurrentIntent, string.Empty));
            var remaining = Math.Max(0f, ctx.Bb.GetOrDefault(TinyTownKeys.ActivityRemainingSeconds, 0f) - NeedTickSeconds);
            ctx.Bb.Set(TinyTownKeys.ActivityRemainingSeconds, remaining);

            if (remaining <= 0.001f)
            {
                CompleteActivity(ctx, ctx.Bb.GetOrDefault(TinyTownKeys.CurrentIntent, string.Empty));
                ctx.Bb.Set(TinyTownKeys.CurrentPhase, "Choose");
            }
        }
    }

    private void ApplyActivityEffects(AiCtx ctx, string intentId)
    {
        if (string.Equals(intentId, TinyTownIntent.DrinkAtWell.Id, StringComparison.Ordinal))
        {
            LowerNeed(ctx, TinyTownKeys.Thirst, 0.18f);
            LowerNeed(ctx, TinyTownKeys.SocialNeed, 0.03f);
        }
        else if (string.Equals(intentId, TinyTownIntent.ShopAtMarket.Id, StringComparison.Ordinal))
        {
            LowerNeed(ctx, TinyTownKeys.Hunger, 0.14f);
            LowerNeed(ctx, TinyTownKeys.SocialNeed, 0.06f);
        }
        else if (string.Equals(intentId, TinyTownIntent.RestAtHome.Id, StringComparison.Ordinal))
        {
            LowerNeed(ctx, TinyTownKeys.RestNeed, 0.16f);
            IncreaseNeed(ctx, TinyTownKeys.Hunger, 0.03f);
            IncreaseNeed(ctx, TinyTownKeys.Thirst, 0.03f);
        }
        else if (string.Equals(intentId, TinyTownIntent.TendGarden.Id, StringComparison.Ordinal))
        {
            LowerNeed(ctx, TinyTownKeys.JoyNeed, 0.16f);
            LowerNeed(ctx, TinyTownKeys.Hunger, 0.04f);
            IncreaseNeed(ctx, TinyTownKeys.RestNeed, 0.02f);
        }
        else if (string.Equals(intentId, TinyTownIntent.Socialize.Id, StringComparison.Ordinal))
        {
            LowerNeed(ctx, TinyTownKeys.SocialNeed, 0.18f);
            LowerNeed(ctx, TinyTownKeys.JoyNeed, 0.06f);
        }
        else if (string.Equals(intentId, TinyTownIntent.Wander.Id, StringComparison.Ordinal)
            || string.Equals(intentId, TinyTownIntent.ReturnHome.Id, StringComparison.Ordinal))
        {
            LowerNeed(ctx, TinyTownKeys.JoyNeed, 0.06f);
            LowerNeed(ctx, TinyTownKeys.RestNeed, 0.04f);
        }
    }

    private void CompleteActivity(AiCtx ctx, string intentId)
    {
        ctx.Bb.Set(TinyTownKeys.LastActivity, intentId);
        ctx.Bb.Set(TinyTownKeys.ActivityCycleIndex, ctx.Bb.GetOrDefault(TinyTownKeys.ActivityCycleIndex, 0) + 1);

        if (string.Equals(intentId, TinyTownIntent.DrinkAtWell.Id, StringComparison.Ordinal))
        {
            ctx.Bb.Set(TinyTownKeys.WellCooldownSeconds, 5.5f);
        }
        else if (string.Equals(intentId, TinyTownIntent.ShopAtMarket.Id, StringComparison.Ordinal))
        {
            ctx.Bb.Set(TinyTownKeys.MarketCooldownSeconds, 6.0f);
        }
        else if (string.Equals(intentId, TinyTownIntent.RestAtHome.Id, StringComparison.Ordinal))
        {
            ctx.Bb.Set(TinyTownKeys.RestCooldownSeconds, 6.5f);
        }
        else if (string.Equals(intentId, TinyTownIntent.TendGarden.Id, StringComparison.Ordinal))
        {
            ctx.Bb.Set(TinyTownKeys.GardenCooldownSeconds, 6.0f);
        }
        else if (string.Equals(intentId, TinyTownIntent.Socialize.Id, StringComparison.Ordinal))
        {
            ctx.Bb.Set(TinyTownKeys.SocialCooldownSeconds, 5.5f);
        }
        else if (string.Equals(intentId, TinyTownIntent.Wander.Id, StringComparison.Ordinal))
        {
            ctx.Bb.Set(TinyTownKeys.WanderCooldownSeconds, 3.0f);
            ctx.Bb.Set(TinyTownKeys.WanderIndex, ctx.Bb.GetOrDefault(TinyTownKeys.WanderIndex, 0) + 1);
        }
        else if (string.Equals(intentId, TinyTownIntent.ReturnHome.Id, StringComparison.Ordinal))
        {
            ctx.Bb.Set(TinyTownKeys.ReturnHomeCooldownSeconds, 4.0f);
        }
    }

    private void UpdateCurrentNeed(AiCtx ctx)
    {
        var thirstUrgency = ctx.Bb.GetOrDefault(TinyTownKeys.Thirst, 0f);
        var hungerUrgency = ctx.Bb.GetOrDefault(TinyTownKeys.Hunger, 0f);
        var restUrgency = ctx.Bb.GetOrDefault(TinyTownKeys.RestNeed, 0f);
        var joyUrgency = ctx.Bb.GetOrDefault(TinyTownKeys.JoyNeed, 0f);
        var socialUrgency = ctx.Bb.GetOrDefault(TinyTownKeys.SocialNeed, 0f);

        var max = thirstUrgency;
        var label = "Thirst";

        if (hungerUrgency > max)
        {
            max = hungerUrgency;
            label = "Hunger";
        }

        if (restUrgency > max)
        {
            max = restUrgency;
            label = "Rest";
        }

        if (joyUrgency > max)
        {
            max = joyUrgency;
            label = "Joy";
        }

        if (socialUrgency > max)
            label = "Social";

        ctx.Bb.Set(TinyTownKeys.CurrentNeed, label);
    }

    private Vector2 ResolveIntentTarget(AiCtx ctx, TinyTownIntent intent)
    {
        if (ReferenceEquals(intent, TinyTownIntent.DrinkAtWell))
            return ResolveDestinationOffset(TinyTownKeys.WellPosition, WellOffsets);
        if (ReferenceEquals(intent, TinyTownIntent.ShopAtMarket))
            return ResolveDestinationOffset(TinyTownKeys.MarketPosition, MarketOffsets);
        if (ReferenceEquals(intent, TinyTownIntent.TendGarden))
            return ResolveDestinationOffset(TinyTownKeys.GardenPosition, GardenOffsets);
        if (ReferenceEquals(intent, TinyTownIntent.RestAtHome))
            return ctx.Bb.GetOrDefault(TinyTownKeys.HomePosition, Vector2.Zero);
        if (ReferenceEquals(intent, TinyTownIntent.ReturnHome))
            return ctx.Bb.GetOrDefault(TinyTownKeys.HomePosition, Vector2.Zero);
        if (ReferenceEquals(intent, TinyTownIntent.Socialize))
        {
            if (WorldNode is TinyTownWorld world
                && world.TryGetVillagerPosition(ctx.Bb.GetOrDefault(TinyTownKeys.SocialBuddyName, string.Empty), out var buddyPosition))
            {
                return buddyPosition + SocialOffset(Profile.Index);
            }

            return ResolveDestinationOffset(TinyTownKeys.MarketPosition, MarketOffsets);
        }

        var wanderIndex = ctx.Bb.GetOrDefault(TinyTownKeys.WanderIndex, Profile.Index);
        return WanderPoints[Math.Abs(wanderIndex) % WanderPoints.Length] + SocialOffset(Profile.Index) * 0.4f;
    }

    private bool TryArrive(Vector2 target) => GetBody().GlobalPosition.DistanceTo(target) <= ArrivalDistance;

    private Vector2 CreateVelocityTo(Vector2 target)
    {
        var from = GetBody().GlobalPosition;
        var delta = target - from;
        return delta.LengthSquared() <= 0.001f
            ? Vector2.Zero
            : delta.Normalized() * (MoveSpeed * Profile.MoveSpeedMultiplier);
    }

    private CharacterBody2D GetBody()
        => GetParent() as CharacterBody2D
            ?? throw new InvalidOperationException("TinyTownVillagerBrain expects to be attached under a CharacterBody2D.");

    private Marker2D ResolveMarker(NodePath path)
        => GetNode<Marker2D>(path);

    private Consideration CommitAware(TinyTownIntent intent, Consideration baseScore)
        => Utility.Score((world, agent) =>
        {
            var currentIntent = agent.Bb.GetOrDefault(TinyTownKeys.CurrentIntent, string.Empty);
            var phase = agent.Bb.GetOrDefault(TinyTownKeys.CurrentPhase, "Choose");
            var score = baseScore.Eval(world, agent);
            if (!string.Equals(currentIntent, intent.Id, StringComparison.Ordinal))
                return score;

            var commitFloor = phase switch
            {
                "Travel" => intent.TravelCommitFloor,
                "Dwell" => intent.DwellCommitFloor,
                _ => 0f
            };

            return MathF.Max(score, commitFloor);
        });

    private Consideration Blend(params (Consideration Consideration, float Weight)[] parts)
        => Utility.Score((world, agent) =>
        {
            var sum = 0f;
            var total = 0f;
            foreach (var (consideration, weight) in parts)
            {
                if (weight <= 0f)
                    continue;

                sum += consideration.Eval(world, agent) * weight;
                total += weight;
            }

            return total <= 0.0001f ? 0f : sum / total;
        });

    private Consideration Constant(float value)
        => Utility.Score((_, _) => value);

    private Consideration CooldownReady(BbKey<float> key, float durationSeconds)
        => Utility.Score((_, agent) =>
        {
            if (durationSeconds <= 0f)
                return 1f;

            var remaining = agent.Bb.GetOrDefault(key, 0f);
            return 1f - Math.Clamp(remaining / durationSeconds, 0f, 1f);
        });

    private Consideration TargetProximity(Func<Vector2> targetResolver)
        => Utility.Score((_, _) =>
        {
            var distance = GetBody().GlobalPosition.DistanceTo(targetResolver());
            return 1f - Math.Clamp(distance / 420f, 0f, 1f);
        });

    private Consideration ModerateNeed(BbKey<float> key, float minInclusive, float maxInclusive)
        => Utility.Score((_, agent) =>
        {
            var value = agent.Bb.GetOrDefault(key, 0f);
            if (value <= minInclusive || value >= maxInclusive)
                return 0f;

            var mid = (minInclusive + maxInclusive) * 0.5f;
            var span = (maxInclusive - minInclusive) * 0.5f;
            return 1f - Math.Clamp(MathF.Abs(value - mid) / MathF.Max(span, 0.0001f), 0f, 1f);
        });

    private Consideration SocialBuddyAvailable()
        => Utility.Score((_, agent) =>
        {
            if (WorldNode is not TinyTownWorld world)
                return 0.25f;

            return world.TryGetVillagerPosition(agent.Bb.GetOrDefault(TinyTownKeys.SocialBuddyName, string.Empty), out Vector2 _)
                ? 1f
                : 0.20f;
        });

    private Consideration DistanceFromHome()
        => Utility.Score((_, agent) =>
        {
            var home = agent.Bb.GetOrDefault(TinyTownKeys.HomePosition, Vector2.Zero);
            var distance = GetBody().GlobalPosition.DistanceTo(home);
            return Math.Clamp(distance / 320f, 0f, 1f);
        });

    private void RecordDecisionSurface(AiCtx ctx, IReadOnlyList<UtilityOption> options)
    {
        string bestId = string.Empty;
        float bestScore = float.MinValue;

        foreach (var option in options)
        {
            var score = option.Score.Eval(ctx.World, ctx.Agent);
            if (score > bestScore)
            {
                bestScore = score;
                bestId = option.Id;
            }
        }

        ctx.Bb.Set(TinyTownKeys.LastDecisionWinner, bestId);
        ctx.Bb.Set(TinyTownKeys.LastDecisionScore, bestScore < 0f ? 0f : bestScore);
    }

    private void EnterTravelPhase(AiCtx ctx, TinyTownIntent intent)
    {
        ctx.Bb.Set(TinyTownKeys.CurrentPhase, "Travel");
        ctx.Bb.Set(TinyTownKeys.CurrentActivity, intent.TravelLabel);
    }

    private void EnterDwellPhase(AiCtx ctx, TinyTownIntent intent)
    {
        ctx.Bb.Set(TinyTownKeys.CurrentPhase, "Dwell");
        ctx.Bb.Set(TinyTownKeys.CurrentActivity, intent.DwellLabel);
    }

    private Vector2 ResolveDestinationOffset(BbKey<Vector2> destinationKey, IReadOnlyList<Vector2> offsets)
    {
        var basePosition = Bb.GetOrDefault(destinationKey, Vector2.Zero);
        return basePosition + offsets[Profile.Index % offsets.Count];
    }

    private float ComputeDwellSeconds(TinyTownIntent intent)
    {
        var cycle = Bb.GetOrDefault(TinyTownKeys.ActivityCycleIndex, 0);
        var variation = ((Profile.Index + cycle + intent.Ordinal) % 3) * 0.35f;
        return intent.MinDwellSeconds + variation;
    }

    private static Vector2 SocialOffset(int index)
    {
        return (index % 4) switch
        {
            0 => new Vector2(24f, 0f),
            1 => new Vector2(-24f, 0f),
            2 => new Vector2(0f, 22f),
            _ => new Vector2(0f, -22f)
        };
    }

    private static void IncreaseNeed(AiCtx ctx, BbKey<float> key, float amount)
        => ctx.Bb.Set(key, Clamp01(ctx.Bb.GetOrDefault(key, 0f) + amount));

    private static void LowerNeed(AiCtx ctx, BbKey<float> key, float amount)
        => ctx.Bb.Set(key, Clamp01(ctx.Bb.GetOrDefault(key, 0f) - amount));

    private void ReduceCooldown(AiCtx ctx, BbKey<float> key)
        => ctx.Bb.Set(key, Math.Max(0f, ctx.Bb.GetOrDefault(key, 0f) - NeedTickSeconds));

    private VillagerProfile Profile
        => _profile ??= VillagerProfile.For(VillagerName);

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private sealed record TinyTownIntent(
        string Id,
        string TravelLabel,
        string DwellLabel,
        string TargetKind,
        float MinDwellSeconds,
        float TravelCommitFloor,
        float DwellCommitFloor,
        int Ordinal)
    {
        public static readonly TinyTownIntent DrinkAtWell = new("DrinkAtWell", "GoToWell", "DrinkAtWell", "Well", 1.85f, 0.68f, 0.74f, 0);
        public static readonly TinyTownIntent ShopAtMarket = new("ShopAtMarket", "GoToMarket", "ShopAtMarket", "Market", 2.35f, 0.66f, 0.74f, 1);
        public static readonly TinyTownIntent RestAtHome = new("RestAtHome", "ReturnHome", "RestAtHome", "Home", 3.20f, 0.72f, 0.82f, 2);
        public static readonly TinyTownIntent TendGarden = new("TendGarden", "TendGarden", "TendGarden", "Garden", 3.10f, 0.66f, 0.72f, 3);
        public static readonly TinyTownIntent Wander = new("Wander", "Wander", "Idle / Think", "Square", 1.10f, 0.60f, 0.68f, 4);
        public static readonly TinyTownIntent Socialize = new("Socialize", "Socialize", "Socialize", "Villager", 2.25f, 0.66f, 0.74f, 5);
        public static readonly TinyTownIntent ReturnHome = new("ReturnHome", "ReturnHome", "Idle / Think", "Home", 1.55f, 0.60f, 0.68f, 6);
    }

    private sealed record VillagerProfile(
        string Name,
        int Index,
        string PersonalityName,
        string SocialBuddyName,
        float StartHungerNeed,
        float StartThirstNeed,
        float StartRestNeed,
        float StartJoyNeed,
        float StartSocialNeed,
        float HungerStep,
        float ThirstStep,
        float RestStep,
        float JoyStep,
        float SocialStep,
        float WellBias,
        float MarketBias,
        float GardenBias,
        float WanderBias,
        float SocialBias,
        float HomeBias,
        float RestBias,
        float MoveSpeedMultiplier)
    {
        public static VillagerProfile For(string villagerName)
            => villagerName switch
            {
                "Maya" => new VillagerProfile("Maya", 0, "Social shopper", "Theo", 0.58f, 0.22f, 0.28f, 0.42f, 0.68f, 0.010f, 0.011f, 0.009f, 0.007f, 0.009f, 0.44f, 0.94f, 0.36f, 0.40f, 0.92f, 0.26f, 0.48f, 1.02f),
                "Theo" => new VillagerProfile("Theo", 1, "Restless wanderer", "Maya", 0.26f, 0.64f, 0.24f, 0.36f, 0.34f, 0.009f, 0.014f, 0.008f, 0.008f, 0.008f, 0.88f, 0.40f, 0.26f, 0.96f, 0.34f, 0.18f, 0.36f, 1.08f),
                "Lina" => new VillagerProfile("Lina", 2, "Quiet gardener", "Maya", 0.32f, 0.24f, 0.30f, 0.76f, 0.22f, 0.007f, 0.010f, 0.008f, 0.010f, 0.007f, 0.34f, 0.28f, 0.98f, 0.32f, 0.24f, 0.42f, 0.40f, 0.94f),
                "Nia" => new VillagerProfile("Nia", 3, "Cozy homebody", "Theo", 0.24f, 0.18f, 0.66f, 0.28f, 0.20f, 0.009f, 0.009f, 0.011f, 0.006f, 0.007f, 0.26f, 0.30f, 0.34f, 0.16f, 0.18f, 0.96f, 0.96f, 0.90f),
                _ => new VillagerProfile(villagerName, 0, "Villager", "Maya", 0.40f, 0.40f, 0.40f, 0.40f, 0.40f, 0.009f, 0.010f, 0.009f, 0.007f, 0.007f, 0.40f, 0.40f, 0.40f, 0.40f, 0.40f, 0.40f, 0.40f, 1f)
            };
    }
}
