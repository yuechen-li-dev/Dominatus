using Dominatus.Core;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.MonoGameConn;
using Dominatus.OptFlow;
using Microsoft.Xna.Framework;

namespace Dominatus.MonoGameRtsDemo;

public sealed class RtsDemoSimulation
{
    public const int WorldWidth = 1920;
    public const int WorldHeight = 1080;
    private const float AttackRange = 145f;
    private const float SensorRange = 1000f;
    private const float ShipSpeed = 95f;
    private const float RetreatSpeed = 135f;
    private const float DamagePerShot = 12f;
    private const float FireCooldownSeconds = 0.85f;
    private readonly List<ShipVisualState> _ships = new();
    private readonly Dictionary<AgentId, ShipVisualState> _byId = new();
    private readonly int _shipCount;

    private RtsDemoSimulation(int shipCount)
    {
        _shipCount = Math.Clamp(shipCount, RtsDemoOptions.MinimumShips, RtsDemoOptions.MaximumShips);
        World = new AiWorld();
        CreateFleet();
    }

    public AiWorld World { get; private set; }
    public IReadOnlyList<ShipVisualState> Ships => _ships;
    public int DominionAlive => _ships.Count(s => s.Alive && s.Faction == RtsFaction.Dominion);
    public int CollectiveAlive => _ships.Count(s => s.Alive && s.Faction == RtsFaction.Collective);

    public static RtsDemoSimulation Create(int shipCount = RtsDemoOptions.DefaultShips) => new(shipCount);

    public void Reset()
    {
        _ships.Clear();
        _byId.Clear();
        World = new AiWorld();
        CreateFleet();
    }

    public void Step(float dt)
    {
        UpdatePerception();
        World.Tick(dt);
        ResolveActions(dt);
    }

    public void UpdatePerception()
    {
        foreach (var ship in _ships)
        {
            if (!ship.Alive)
            {
                ship.Agent.Bb.Set(MonoGameBbKeys.Visible, false);
                ship.Agent.Bb.Set(RtsDemoKeys.EnemyInRange, false);
                continue;
            }

            var nearest = FindNearestEnemy(ship);
            var distance = nearest is null ? SensorRange : Vector2.Distance(ship.Position, nearest.Position);
            var targetSensed = nearest is not null && distance <= SensorRange;
            ship.TargetId = targetSensed ? nearest!.AgentId : null;

            ship.Agent.Bb.Set(RtsDemoKeys.HullFraction, ship.HullFraction);
            ship.Agent.Bb.Set(RtsDemoKeys.NearestEnemyDistance, targetSensed ? distance : SensorRange);
            ship.Agent.Bb.Set(RtsDemoKeys.EnemyInRange, targetSensed && distance <= AttackRange);
            ship.Agent.Bb.Set(RtsDemoKeys.CooldownReady, ship.Cooldown <= 0f);
            if (targetSensed)
                ship.Agent.Bb.Set(RtsDemoKeys.TargetId, nearest!.AgentId);
            else
                ship.Agent.Bb.Remove(RtsDemoKeys.TargetId);

            ship.Agent.Bb.Set(MonoGameBbKeys.Position, ship.Position);
            ship.Agent.Bb.Set(MonoGameBbKeys.Velocity, ship.Velocity);
            ship.Agent.Bb.Set(MonoGameBbKeys.Visible, true);
            World.SetPublic(ship.AgentId, new AgentSnapshot(ship.AgentId, (int)ship.Faction, new System.Numerics.Vector3(ship.Position.X, ship.Position.Y, 0f), true));
        }
    }

    public void ResolveActions(float dt)
    {
        foreach (var ship in _ships)
        {
            if (!ship.Alive)
                continue;

            ship.Cooldown = MathF.Max(0f, ship.Cooldown - dt);
            var action = ship.Agent.Bb.GetOrDefault(RtsDemoKeys.CurrentAction, "Idle");
            var target = ship.TargetId is AgentId id && _byId.TryGetValue(id, out var found) && found.Alive ? found : null;

            ship.Velocity = action switch
            {
                "Retreat" => DirectionAway(ship, target) * RetreatSpeed,
                "Advance" => DirectionToward(ship, target) * ShipSpeed,
                "Attack" => Vector2.Zero,
                "HoldFormation" => FormationDrift(ship),
                _ => Vector2.Zero
            };

            if (action == "Attack" && target is not null && ship.Cooldown <= 0f && Vector2.Distance(ship.Position, target.Position) <= AttackRange)
            {
                target.Hull = MathF.Max(0f, target.Hull - DamagePerShot);
                ship.Cooldown = FireCooldownSeconds;
                if (target.Hull <= 0f)
                    Kill(target);
            }

            ship.Position = ClampToWorld(ship.Position + ship.Velocity * dt);
            ship.Agent.Bb.Set(MonoGameBbKeys.Position, ship.Position);
            ship.Agent.Bb.Set(MonoGameBbKeys.Velocity, ship.Velocity);
        }
    }

    private void CreateFleet()
    {
        var dominionCount = _shipCount / 2;
        var collectiveCount = _shipCount - dominionCount;
        SpawnFaction(RtsFaction.Dominion, dominionCount, new Vector2(220f, 235f), 1);
        SpawnFaction(RtsFaction.Collective, collectiveCount, new Vector2(WorldWidth - 220f, WorldHeight - 235f), -1);
    }

    private void SpawnFaction(RtsFaction faction, int count, Vector2 anchor, int direction)
    {
        var columns = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(count)));
        for (var i = 0; i < count; i++)
        {
            var row = i / columns;
            var col = i % columns;
            var offset = new Vector2(col * 42f * direction, row * 42f);
            var position = anchor + offset;
            var agent = CreateShipAgent();
            World.Add(agent);

            var ship = new ShipVisualState
            {
                AgentId = agent.Id,
                Index = _ships.Count,
                Faction = faction,
                Agent = agent,
                Position = position,
                Velocity = Vector2.Zero,
                Cooldown = (i % 5) * 0.1f
            };

            agent.Bb.Set(RtsDemoKeys.ShipIndex, ship.Index);
            agent.Bb.Set(RtsDemoKeys.Faction, faction.ToString());
            agent.Bb.Set(RtsDemoKeys.CurrentAction, "Idle");
            agent.Bb.Set(MonoGameBbKeys.Position, position);
            agent.Bb.Set(MonoGameBbKeys.Velocity, Vector2.Zero);
            agent.Bb.Set(MonoGameBbKeys.DebugLabel, $"{faction} Idle");
            agent.Bb.Set(MonoGameBbKeys.Visible, true);

            _ships.Add(ship);
            _byId[ship.AgentId] = ship;
        }
    }

    private static AiAgent CreateShipAgent()
    {
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = DecideNode });
        graph.Add(new HfsmStateDef { Id = "Advance", Node = ctx => ActionNode(ctx, "Advance") });
        graph.Add(new HfsmStateDef { Id = "Attack", Node = ctx => ActionNode(ctx, "Attack") });
        graph.Add(new HfsmStateDef { Id = "Retreat", Node = ctx => ActionNode(ctx, "Retreat") });
        graph.Add(new HfsmStateDef { Id = "HoldFormation", Node = ctx => ActionNode(ctx, "HoldFormation") });
        return new AiAgent(new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true }));
    }

    private static IEnumerator<AiStep> DecideNode(AiCtx ctx)
    {
        while (true)
        {
            ctx.Bb.Set(RtsDemoKeys.UsedAiDecide, true);
            yield return Ai.Decide(new DecisionSlot($"RtsDemo.{ctx.Agent.Id.Value}.Action"),
            [
                Ai.Option("Retreat", new Consideration((_, a) => ScoreRetreat(a)), "Retreat"),
                Ai.Option("Attack", new Consideration((_, a) => ScoreAttack(a)), "Attack"),
                Ai.Option("Advance", new Consideration((_, a) => ScoreAdvance(a)), "Advance"),
                Ai.Option("HoldFormation", Consideration.Constant(0.12f), "HoldFormation")
            ], hysteresis: 0.03f, minCommitSeconds: 0.05f, tieEpsilon: 0.0001f);
        }
    }

    private static IEnumerator<AiStep> ActionNode(AiCtx ctx, string action)
    {
        while (true)
        {
            ctx.Bb.Set(RtsDemoKeys.CurrentAction, action);
            var faction = ctx.Bb.GetOrDefault(RtsDemoKeys.Faction, "Ship");
            ctx.Bb.Set(MonoGameBbKeys.DebugLabel, $"{faction} {action}");
            yield return Ai.Steady(action);
        }
    }

    private static float ScoreRetreat(AiAgent agent)
    {
        var hull = agent.Bb.GetOrDefault(RtsDemoKeys.HullFraction, 1f);
        var distance = agent.Bb.GetOrDefault(RtsDemoKeys.NearestEnemyDistance, SensorRange);
        var danger = 1f - Math.Clamp(distance / 260f, 0f, 1f);
        return Math.Clamp((1f - hull) * 1.35f + danger * 0.35f, 0f, 1f);
    }

    private static float ScoreAttack(AiAgent agent)
    {
        if (!agent.Bb.GetOrDefault(RtsDemoKeys.EnemyInRange, false))
            return 0f;

        return agent.Bb.GetOrDefault(RtsDemoKeys.CooldownReady, false) ? 1f : 0.65f;
    }

    private static float ScoreAdvance(AiAgent agent)
    {
        var distance = agent.Bb.GetOrDefault(RtsDemoKeys.NearestEnemyDistance, SensorRange);
        var hull = agent.Bb.GetOrDefault(RtsDemoKeys.HullFraction, 1f);
        var needToClose = Math.Clamp(distance / SensorRange, 0.2f, 1f);
        return hull < 0.25f ? 0.05f : needToClose * 0.82f;
    }

    private ShipVisualState? FindNearestEnemy(ShipVisualState ship)
    {
        ShipVisualState? best = null;
        var bestDistance = float.MaxValue;
        foreach (var other in _ships)
        {
            if (!other.Alive || other.Faction == ship.Faction)
                continue;

            var distance = Vector2.DistanceSquared(ship.Position, other.Position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = other;
            }
        }

        return best;
    }

    private static Vector2 DirectionToward(ShipVisualState ship, ShipVisualState? target)
        => NormalizeOrZero((target?.Position ?? Center) - ship.Position);

    private static Vector2 DirectionAway(ShipVisualState ship, ShipVisualState? target)
    {
        var fallback = ship.Faction == RtsFaction.Dominion ? new Vector2(-1f, 0f) : new Vector2(1f, 0f);
        return NormalizeOrZero(ship.Position - (target?.Position ?? Center), fallback);
    }

    private static Vector2 FormationDrift(ShipVisualState ship)
    {
        var desiredX = ship.Faction == RtsFaction.Dominion ? WorldWidth * 0.38f : WorldWidth * 0.62f;
        return NormalizeOrZero(new Vector2(desiredX, ship.Position.Y) - ship.Position) * 25f;
    }

    private static Vector2 NormalizeOrZero(Vector2 vector, Vector2 fallback = default)
    {
        if (vector.LengthSquared() < 0.0001f)
            return fallback;

        vector.Normalize();
        return vector;
    }

    private static Vector2 ClampToWorld(Vector2 value) => new(Math.Clamp(value.X, 20f, WorldWidth - 20f), Math.Clamp(value.Y, 20f, WorldHeight - 20f));

    private static Vector2 Center => new(WorldWidth / 2f, WorldHeight / 2f);

    private static void Kill(ShipVisualState ship)
    {
        ship.Alive = false;
        ship.Velocity = Vector2.Zero;
        ship.Agent.Bb.Set(MonoGameBbKeys.Visible, false);
        ship.Agent.Bb.Set(MonoGameBbKeys.DebugLabel, $"{ship.Faction} Destroyed");
    }
}
