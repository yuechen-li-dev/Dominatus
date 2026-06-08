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
    public const float ScalePixelsPerUnit = 4.5f;
    private const float RetreatSpeedMultiplier = 1.35f;
    private const float SafeRetreatRangeMultiplier = 2.4f;
    private const float EdgeRecoveryMargin = 90f;
    private const float CooldownSecondsPerBenchmarkTick = 0.28f;
    private const float SeparationForceScale = 80f;
    private const float MaxSeparationSpeed = 42f;
    private const float FormationDriftSpeed = 25f;
    private const float SpatialCellSize = 160f;
    private readonly List<ShipVisualState> _ships = new();
    private readonly Dictionary<AgentId, ShipVisualState> _byId = new();
    private readonly RtsDemoSpatialGrid _spatialGrid = new(SpatialCellSize);
    private readonly int _shipCount;

    private static readonly (ShipClass Class, float Weight)[] DominionComposition =
    [
        (ShipClass.ScoutFrigate, 0.15f),
        (ShipClass.MissileCorvette, 0.25f),
        (ShipClass.RailgunDestroyer, 0.25f),
        (ShipClass.Carrier, 0.10f),
        (ShipClass.RepairTender, 0.10f),
        (ShipClass.CommandCruiser, 0.15f)
    ];

    private static readonly (ShipClass Class, float Weight)[] CollectiveComposition =
    [
        (ShipClass.NeedleDrone, 0.35f),
        (ShipClass.SporeFrigate, 0.20f),
        (ShipClass.SynapseCruiser, 0.15f),
        (ShipClass.Regenerator, 0.10f),
        (ShipClass.Harvester, 0.10f),
        (ShipClass.HiveArk, 0.10f)
    ];

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
        _spatialGrid.Rebuild(_ships);

        foreach (var ship in _ships)
        {
            ship.SeparationCandidates.Clear();

            if (!ship.Alive)
            {
                ship.Agent.Bb.Set(MonoGameBbKeys.Visible, false);
                ship.Agent.Bb.Set(RtsDemoKeys.EnemyInRange, false);
                ship.TargetId = null;
                continue;
            }

            foreach (var candidateId in _spatialGrid.QueryCandidateIds(ship.Position, MathF.Max(ship.SensorRange, ship.SeparationRadius)))
            {
                if (candidateId == ship.AgentId || !_byId.TryGetValue(candidateId, out var candidate) || !candidate.Alive)
                    continue;

                if (candidate.Faction == ship.Faction && Vector2.DistanceSquared(ship.Position, candidate.Position) <= ship.SeparationRadius * ship.SeparationRadius)
                    ship.SeparationCandidates.Add(candidateId);
            }

            var nearest = FindNearestEnemy(ship);
            var distance = nearest is null ? ship.SensorRange : Vector2.Distance(ship.Position, nearest.Position);
            ship.TargetId = nearest?.AgentId;

            ship.Agent.Bb.Set(RtsDemoKeys.HullFraction, ship.HullFraction);
            ship.Agent.Bb.Set(RtsDemoKeys.NearestEnemyDistance, nearest is null ? ship.SensorRange : distance);
            ship.Agent.Bb.Set(RtsDemoKeys.EnemyInRange, nearest is not null && distance <= ship.AttackRange);
            ship.Agent.Bb.Set(RtsDemoKeys.CooldownReady, ship.Cooldown <= 0f);
            if (nearest is not null)
                ship.Agent.Bb.Set(RtsDemoKeys.TargetId, nearest.AgentId);
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
            ship.FiredThisFrame = false;
            ship.LaserTargetPos = null;
        }

        foreach (var ship in _ships)
        {
            if (!ship.Alive)
                continue;

            ship.Cooldown = MathF.Max(0f, ship.Cooldown - dt);
            var action = ship.Agent.Bb.GetOrDefault(RtsDemoKeys.CurrentAction, "Idle");
            var target = ship.TargetId is AgentId id && _byId.TryGetValue(id, out var found) && found.Alive ? found : null;
            var separation = AlliedSeparation(ship);

            var desiredVelocity = action switch
            {
                "Retreat" => RetreatVelocity(ship, target, dt) + separation * 0.55f,
                "Advance" => DirectionToward(ship, target) * ship.Speed + separation,
                "Attack" => separation * 0.75f,
                "HoldFormation" => FormationDrift(ship) + separation,
                _ => separation * 0.4f
            };

            ship.Velocity = ClampMagnitude(desiredVelocity, ship.Speed * RetreatSpeedMultiplier);

            if (action == "Attack" && target is not null && ship.Cooldown <= 0f && Vector2.Distance(ship.Position, target.Position) <= ship.AttackRange)
            {
                target.Hull = MathF.Max(0f, target.Hull - ship.Def.Damage);
                ship.Cooldown = ship.Def.CooldownTicks * CooldownSecondsPerBenchmarkTick;
                ship.FiredThisFrame = true;
                ship.LaserTargetPos = target.Position;
                if (target.Hull <= 0f)
                    Kill(target);
            }

            ship.Position = ClampToWorld(ship.Position + ship.Velocity * dt);
            ship.Agent.Bb.Set(MonoGameBbKeys.Position, ship.Position);
            ship.Agent.Bb.Set(MonoGameBbKeys.Velocity, ship.Velocity);
        }
    }

    public static Vector2 CalculateFormationDrift(ShipVisualState ship) => FormationDrift(ship);

    private void CreateFleet()
    {
        var dominionCount = _shipCount / 2;
        var collectiveCount = _shipCount - dominionCount;
        SpawnFaction(RtsFaction.Dominion, dominionCount, new Vector2(WorldWidth * 0.18f, WorldHeight * 0.5f), 1);
        SpawnFaction(RtsFaction.Collective, collectiveCount, new Vector2(WorldWidth * 0.82f, WorldHeight * 0.5f), -1);
    }

    private void SpawnFaction(RtsFaction faction, int count, Vector2 anchor, int direction)
    {
        var columns = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(count)));
        var rows = Math.Max(1, (int)MathF.Ceiling(count / (float)columns));
        var spacing = 54f;
        var doctrine = DoctrineProfile.For(faction);

        for (var i = 0; i < count; i++)
        {
            var row = i / columns;
            var col = i % columns;
            var centeredCol = col - (columns - 1) * 0.5f;
            var centeredRow = row - (rows - 1) * 0.5f;
            var wedge = MathF.Abs(centeredRow) * 16f * direction;
            var position = ClampToWorld(anchor + new Vector2((centeredCol * spacing + wedge) * direction, centeredRow * spacing));
            var shipClass = SelectClass(faction, i, count);
            var def = ShipClassDefinition.Get(shipClass);
            var agent = CreateShipAgent();
            World.Add(agent);

            var ship = new ShipVisualState
            {
                AgentId = agent.Id,
                Index = _ships.Count,
                Faction = faction,
                Agent = agent,
                Class = shipClass,
                Def = def,
                Position = position,
                HomePosition = position,
                Velocity = Vector2.Zero,
                Hull = def.Hull + def.ShieldOrCarapace,
                Cooldown = (i % 5) * 0.1f
            };

            agent.Bb.Set(RtsDemoKeys.ShipIndex, ship.Index);
            agent.Bb.Set(RtsDemoKeys.Faction, faction.ToString());
            agent.Bb.Set(RtsDemoKeys.ShipClass, shipClass);
            agent.Bb.Set(RtsDemoKeys.AttackRange, ship.AttackRange);
            agent.Bb.Set(RtsDemoKeys.ShipSpeed, ship.Speed);
            agent.Bb.Set(RtsDemoKeys.IsRepairShip, def.RepairAmount > 0f);
            agent.Bb.Set(RtsDemoKeys.IsCarrier, shipClass is ShipClass.Carrier or ShipClass.HiveArk);
            agent.Bb.Set(RtsDemoKeys.Aggression, doctrine.Aggression);
            agent.Bb.Set(RtsDemoKeys.RepairPriority, doctrine.RepairPriority);
            agent.Bb.Set(RtsDemoKeys.CurrentAction, "Idle");
            agent.Bb.Set(MonoGameBbKeys.Position, position);
            agent.Bb.Set(MonoGameBbKeys.Velocity, Vector2.Zero);
            agent.Bb.Set(MonoGameBbKeys.DebugLabel, $"{faction} {shipClass} Idle");
            agent.Bb.Set(MonoGameBbKeys.Visible, true);

            _ships.Add(ship);
            _byId[ship.AgentId] = ship;
        }
    }

    private static ShipClass SelectClass(RtsFaction faction, int index, int count)
    {
        var composition = faction == RtsFaction.Dominion ? DominionComposition : CollectiveComposition;
        if (count <= 1)
            return composition[0].Class;

        var sample = (index + 0.5f) / count;
        var cumulative = 0f;
        foreach (var (shipClass, weight) in composition)
        {
            cumulative += weight;
            if (sample <= cumulative)
                return shipClass;
        }

        return composition[^1].Class;
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
                Ai.Option("HoldFormation", new Consideration((_, a) => ScoreHoldFormation(a)), "HoldFormation")
            ], hysteresis: 0.03f, minCommitSeconds: 0.05f, tieEpsilon: 0.0001f);
        }
    }

    private static IEnumerator<AiStep> ActionNode(AiCtx ctx, string action)
    {
        while (true)
        {
            ctx.Bb.Set(RtsDemoKeys.CurrentAction, action);
            var faction = ctx.Bb.GetOrDefault(RtsDemoKeys.Faction, "Ship");
            var shipClass = ctx.Bb.GetOrDefault(RtsDemoKeys.ShipClass, ShipClass.ScoutFrigate);
            ctx.Bb.Set(MonoGameBbKeys.DebugLabel, $"{faction} {shipClass} {action}");
            yield return Ai.Steady(action);
        }
    }

    private static float ScoreRetreat(AiAgent agent)
    {
        if (!agent.Bb.TryGet(RtsDemoKeys.TargetId, out AgentId _))
            return 0f;

        var hull = agent.Bb.GetOrDefault(RtsDemoKeys.HullFraction, 1f);
        var attackRange = agent.Bb.GetOrDefault(RtsDemoKeys.AttackRange, 160f);
        var distance = agent.Bb.GetOrDefault(RtsDemoKeys.NearestEnemyDistance, attackRange * SafeRetreatRangeMultiplier);
        var aggression = agent.Bb.GetOrDefault(RtsDemoKeys.Aggression, 1f);
        var damage = 1f - hull;
        var safeRetreatDistance = attackRange * SafeRetreatRangeMultiplier;
        if (distance >= safeRetreatDistance)
            return damage >= 0.65f ? 0.05f : 0f;

        var danger = 1f - Math.Clamp(distance / safeRetreatDistance, 0f, 1f);
        if (damage < 0.18f)
            return danger * 0.14f / aggression;

        return Math.Clamp((damage * 0.95f + danger * 0.45f) * danger / aggression, 0f, 1f);
    }

    private static float ScoreAttack(AiAgent agent)
    {
        if (!agent.Bb.GetOrDefault(RtsDemoKeys.EnemyInRange, false))
            return 0f;

        var aggression = agent.Bb.GetOrDefault(RtsDemoKeys.Aggression, 1f);
        return (agent.Bb.GetOrDefault(RtsDemoKeys.CooldownReady, false) ? 0.95f : 0.62f) * aggression;
    }

    private static float ScoreAdvance(AiAgent agent)
    {
        var attackRange = agent.Bb.GetOrDefault(RtsDemoKeys.AttackRange, 160f);
        var distance = agent.Bb.GetOrDefault(RtsDemoKeys.NearestEnemyDistance, attackRange * 2.5f);
        var hull = agent.Bb.GetOrDefault(RtsDemoKeys.HullFraction, 1f);
        var aggression = agent.Bb.GetOrDefault(RtsDemoKeys.Aggression, 1f);
        var needToClose = Math.Clamp(distance / MathF.Max(attackRange * 2.4f, 1f), 0.25f, 1f);
        if (hull < 0.25f)
            return 0.05f;
        if (hull < 0.45f && distance < attackRange * SafeRetreatRangeMultiplier)
            return 0.16f;

        return Math.Clamp(needToClose * 0.74f * aggression, 0f, 0.98f);
    }

    private static float ScoreHoldFormation(AiAgent agent)
    {
        if (agent.Bb.TryGet(RtsDemoKeys.TargetId, out AgentId _))
            return 0.08f;

        return 0.16f;
    }

    private ShipVisualState? FindNearestEnemy(ShipVisualState ship)
    {
        ShipVisualState? best = null;
        var bestDistance = ship.SensorRange * ship.SensorRange;
        foreach (var candidateId in _spatialGrid.QueryCandidateIds(ship.Position, ship.SensorRange))
        {
            if (!_byId.TryGetValue(candidateId, out var other) || !other.Alive || other.Faction == ship.Faction)
                continue;

            var distance = Vector2.DistanceSquared(ship.Position, other.Position);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = other;
            }
        }

        return best;
    }

    private static Vector2 DirectionToward(ShipVisualState ship, ShipVisualState? target)
        => NormalizeOrZero((target?.Position ?? Center) - ship.Position);

    private static Vector2 RetreatVelocity(ShipVisualState ship, ShipVisualState? target, float dt)
    {
        var rallyDirection = DirectionTowardRally(ship);
        var awayDirection = target is null
            ? rallyDirection
            : NormalizeOrZero(ship.Position - target.Position, rallyDirection);

        var edgePressure = EdgePressure(ship.Position);
        var direction = NormalizeOrZero(awayDirection * (1f - edgePressure * 0.65f) + rallyDirection * (0.35f + edgePressure), rallyDirection);
        var speed = ship.Speed * RetreatSpeedMultiplier;
        var proposed = ship.Position + direction * speed * dt;
        if (ClampToWorld(proposed) != proposed)
            direction = NormalizeOrZero(rallyDirection * 2f + awayDirection * 0.2f, rallyDirection);

        return direction * speed;
    }

    private static Vector2 FormationDrift(ShipVisualState ship)
    {
        var desiredX = ship.Faction == RtsFaction.Dominion ? WorldWidth * 0.38f : WorldWidth * 0.62f;
        var desired = new Vector2(desiredX, ship.HomePosition.Y);
        return NormalizeOrZero(desired - ship.Position) * FormationDriftSpeed;
    }

    private static Vector2 DirectionTowardRally(ShipVisualState ship)
    {
        var rally = new Vector2(ship.Faction == RtsFaction.Dominion ? WorldWidth * 0.38f : WorldWidth * 0.62f, ship.HomePosition.Y);
        return NormalizeOrZero(rally - ship.Position, ship.Faction == RtsFaction.Dominion ? Vector2.UnitX : -Vector2.UnitX);
    }

    private Vector2 AlliedSeparation(ShipVisualState ship)
    {
        var force = Vector2.Zero;
        foreach (var candidateId in ship.SeparationCandidates)
        {
            if (!_byId.TryGetValue(candidateId, out var other) || !other.Alive || other.Faction != ship.Faction)
                continue;

            var delta = ship.Position - other.Position;
            var distanceSquared = delta.LengthSquared();
            if (distanceSquared < 0.0001f)
            {
                delta = ship.Index <= other.Index ? new Vector2(-1f, 0f) : new Vector2(1f, 0f);
                distanceSquared = 1f;
            }

            var radius = MathF.Max(ship.SeparationRadius, other.SeparationRadius);
            if (distanceSquared > radius * radius)
                continue;

            var distance = MathF.Sqrt(distanceSquared);
            var falloff = 1f - Math.Clamp(distance / radius, 0f, 1f);
            force += NormalizeOrZero(delta) * falloff * SeparationForceScale;
        }

        return ClampMagnitude(force, MaxSeparationSpeed);
    }

    private static float EdgePressure(Vector2 position)
    {
        var nearestEdge = MathF.Min(MathF.Min(position.X - 20f, WorldWidth - 20f - position.X), MathF.Min(position.Y - 20f, WorldHeight - 20f - position.Y));
        return 1f - Math.Clamp(nearestEdge / EdgeRecoveryMargin, 0f, 1f);
    }

    private static Vector2 NormalizeOrZero(Vector2 vector, Vector2 fallback = default)
    {
        if (vector.LengthSquared() < 0.0001f)
            return fallback;

        vector.Normalize();
        return vector;
    }

    private static Vector2 ClampMagnitude(Vector2 vector, float max)
    {
        if (max <= 0f)
            return Vector2.Zero;

        var lengthSquared = vector.LengthSquared();
        if (lengthSquared <= max * max)
            return vector;

        vector.Normalize();
        return vector * max;
    }

    private static Vector2 ClampToWorld(Vector2 value) => new(Math.Clamp(value.X, 20f, WorldWidth - 20f), Math.Clamp(value.Y, 20f, WorldHeight - 20f));

    private static Vector2 Center => new(WorldWidth / 2f, WorldHeight / 2f);

    private static void Kill(ShipVisualState ship)
    {
        ship.Alive = false;
        ship.Velocity = Vector2.Zero;
        ship.TargetId = null;
        ship.Agent.Bb.Set(MonoGameBbKeys.Visible, false);
        ship.Agent.Bb.Set(MonoGameBbKeys.DebugLabel, $"{ship.Faction} {ship.Class} Destroyed");
    }
}
