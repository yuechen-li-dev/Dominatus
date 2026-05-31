using Dominatus.Core.Runtime;

namespace Dominatus.RTSBenchmark.Simulation;

public sealed class BattleSimulation
{
    private readonly AiWorld _world = new();
    private readonly List<ShipState> _ships;
    private readonly Dictionary<int, ShipState> _byId;
    private readonly Dictionary<int, AiAgent> _agents = new();
    private readonly Dictionary<int, int> _shipToAgentId = new();
    private readonly List<ShipAction> _actions = new();
    private readonly BenchmarkMetrics _metrics = new();
    private readonly List<string> _checkpoints = new();
    private readonly TextWriter? _output;
    private readonly bool _writeCheckpoints;
    private readonly int _checkpointInterval;
    private readonly int _initialShips;

    public BattleSimulation(int shipCount, int checkpointInterval, bool writeCheckpoints, TextWriter? output)
    {
        _ships = FleetFactory.CreateFleets(shipCount);
        _byId = _ships.ToDictionary(s => s.Id);
        _initialShips = _ships.Count;
        _checkpointInterval = checkpointInterval;
        _writeCheckpoints = writeCheckpoints;
        _output = output;

        foreach (var ship in _ships)
        {
            var agent = ShipAgentFactory.Create(ship);
            _world.Add(agent);
            _agents[ship.Id] = agent;
            _shipToAgentId[ship.Id] = agent.Id.Value;
        }
    }

    public IReadOnlyList<ShipState> Ships => _ships;
    public BenchmarkMetrics Metrics => _metrics;
    public IReadOnlyList<string> Checkpoints => _checkpoints;

    public void RunTicks(int ticks)
    {
        for (var tick = 1; tick <= ticks; tick++)
            RunTick(tick);
    }

    public void RunTick(int tick)
    {
        _actions.Clear();
        DecrementCooldowns();
        SensorPhase();
        DecisionPhase(tick);
        ResolutionPhase(tick);
        EventPhase(tick);
        if (_writeCheckpoints && tick % _checkpointInterval == 0)
            WriteCheckpoint(tick);
    }

    private void DecrementCooldowns()
    {
        foreach (var ship in _ships)
        {
            if (ship.Alive && ship.CooldownRemaining > 0)
                ship.CooldownRemaining--;
        }
    }

    private void SensorPhase()
    {
        foreach (var ship in _ships)
        {
            if (!ship.Alive) continue;
            var def = ShipClassDefinition.Get(ship.Class);
            var nearest = FindNearestEnemy(ship, def.SensorRange * 1.5f);
            var vulnerableAlly = FindVulnerableAlly(ship, Math.Max(32f, def.Range + 16f));
            var agent = _agents[ship.Id];
            var ownHullFraction = Math.Clamp(ship.Hull / Math.Max(1f, def.Hull), 0f, 1f);
            var threat = ComputeThreat(ship, nearest);
            var targetId = nearest?.Id ?? -1;
            if (agent.Bb.GetOrDefault(BenchmarkBlackboardKeys.FocusTargetId, -1) > 0)
                targetId = agent.Bb.GetOrDefault(BenchmarkBlackboardKeys.FocusTargetId, targetId);

            agent.Bb.Set(BenchmarkBlackboardKeys.OwnHullFraction, ownHullFraction);
            agent.Bb.Set(BenchmarkBlackboardKeys.ThreatScore, threat);
            agent.Bb.Set(BenchmarkBlackboardKeys.NearestVisibleEnemyId, nearest?.Id ?? -1);
            agent.Bb.Set(BenchmarkBlackboardKeys.TargetValue, nearest is null ? 0f : ShipClassDefinition.Get(nearest.Class).RoleWeight / 3f);
            agent.Bb.Set(BenchmarkBlackboardKeys.VulnerableAllyId, vulnerableAlly?.Id ?? -1);
            agent.Bb.Set(BenchmarkBlackboardKeys.CooldownReady, ship.CooldownRemaining <= 0);
            agent.Bb.Set(BenchmarkBlackboardKeys.EnemyInWeaponRange, nearest is not null && Distance(ship, nearest) <= def.Range);
        }
    }

    private void DecisionPhase(int tick)
    {
        foreach (var ship in _ships)
        {
            if (!ship.Alive) continue;
            var agent = _agents[ship.Id];
            agent.Tick(_world);
            _metrics.AgentTicks++;
            _metrics.DecisionsEvaluated += 9;
            var selected = agent.Bb.GetOrDefault(BenchmarkBlackboardKeys.CurrentAction, nameof(ShipActionType.Idle));
            var type = Enum.TryParse<ShipActionType>(selected, out var parsed) ? parsed : ShipActionType.Idle;
            ship.CurrentAction = type.ToString();
            ship.TargetId = ChooseTargetForAction(ship, type);
            _actions.Add(new ShipAction(tick, ship.Id, ship.Faction, type, ship.TargetId, PriorityFor(type)));
            _metrics.ActionsEmitted++;
            if (ship.Faction == Faction.Dominion) _metrics.DominionActions++; else _metrics.CollectiveActions++;
        }
    }

    private void ResolutionPhase(int tick)
    {
        foreach (var action in _actions.OrderBy(a => a.Tick)
                     .ThenBy(a => a.Priority)
                     .ThenBy(a => a.Faction)
                     .ThenBy(a => a.ActorId)
                     .ThenBy(a => a.TargetId ?? -1)
                     .ThenBy(a => a.Type))
        {
            if (!_byId.TryGetValue(action.ActorId, out var actor) || !actor.Alive) continue;
            var def = ShipClassDefinition.Get(actor.Class);
            switch (action.Type)
            {
                case ShipActionType.Advance:
                    MoveToward(actor, FindNearestEnemy(actor, 10_000f), def.Speed);
                    break;
                case ShipActionType.Retreat:
                    MoveAway(actor, FindNearestEnemy(actor, 10_000f), def.Speed * 1.35f);
                    break;
                case ShipActionType.FocusFire:
                    TryDamage(actor, action.TargetId, def.Damage, def.Range, def.CooldownTicks, tick);
                    break;
                case ShipActionType.RepairAlly:
                    TryRepair(actor, action.TargetId, def.RepairAmount, Math.Max(def.Range, 30f));
                    break;
                case ShipActionType.LaunchDrone:
                    TryDamage(actor, action.TargetId, def.Damage + 16f, def.Range + 10f, Math.Max(1, def.CooldownTicks), tick);
                    break;
                case ShipActionType.Regenerate:
                    Regenerate(actor, def);
                    break;
                case ShipActionType.ScreenHighValue:
                case ShipActionType.HoldFormation:
                    Hold(actor, def.Speed * 0.25f);
                    break;
            }
        }
    }

    private void EventPhase(int tick)
    {
        foreach (var ship in _ships)
        {
            if (!ship.Alive) continue;
            var def = ShipClassDefinition.Get(ship.Class);
            if (ship.TargetId is int targetId && targetId > 0 && def.CommandRadius > 0 && tick % 5 == 0)
            {
                DeliverToFaction(ship.Faction, new CommandFocusOrder(tick, ship.Id, targetId), ship.Faction);
                DeliverToFaction(ship.Faction, new TargetSpotted(tick, ship.Id, targetId, Opposing(ship.Faction)), ship.Faction);
            }

            var maxHull = def.Hull;
            if (ship.Hull / maxHull < 0.45f && tick % 7 == 0)
                DeliverToFaction(ship.Faction, new RepairRequested(tick, ship.Id, ship.Id), ship.Faction);
        }
    }

    private void DeliverToFaction<T>(Faction faction, T message, Faction sourceFaction) where T : notnull
    {
        foreach (var ship in _ships)
        {
            if (!ship.Alive || ship.Faction != faction) continue;
            if (_world.Mail.Send(new AgentId(_shipToAgentId[ship.Id]), message))
            {
                _metrics.EventsDelivered++;
                if (sourceFaction == Faction.Dominion) _metrics.DominionEvents++; else _metrics.CollectiveEvents++;
                if (message is CommandFocusOrder order)
                    _agents[ship.Id].Bb.Set(BenchmarkBlackboardKeys.FocusTargetId, order.TargetShipId);
            }
        }
    }

    private void PublishDestroyed(int tick, ShipState victim, int attackerId)
    {
        _metrics.DestroyedShips++;
        DeliverToFaction(Opposing(victim.Faction), new ShipDestroyed(tick, victim.Id, victim.Faction), Opposing(victim.Faction));
        DeliverToFaction(victim.Faction, new AllyUnderFire(tick, victim.Id, attackerId), victim.Faction);
        if (victim.Class == ShipClass.SynapseCruiser)
            DeliverToFaction(Faction.Collective, new SynapseLost(tick, victim.Id), Faction.Collective);
    }

    private void TryDamage(ShipState actor, int? targetId, float damage, float range, int cooldownTicks, int tick)
    {
        if (actor.CooldownRemaining > 0 || targetId is not int id || !_byId.TryGetValue(id, out var target) || !target.Alive) return;
        if (Distance(actor, target) > range) return;

        var remaining = damage;
        if (target.ShieldOrCarapace > 0)
        {
            var shieldHit = Math.Min(target.ShieldOrCarapace, remaining);
            target.ShieldOrCarapace -= shieldHit;
            remaining -= shieldHit;
        }
        target.Hull -= remaining;
        actor.CooldownRemaining = cooldownTicks;
        _metrics.DamageEvents++;
        if (target.Hull <= 0 && target.Alive)
        {
            target.Alive = false;
            target.Hull = 0;
            target.CurrentAction = "Destroyed";
            PublishDestroyed(tick, target, actor.Id);
        }
    }

    private void TryRepair(ShipState actor, int? targetId, float amount, float range)
    {
        if (amount <= 0 || targetId is not int id || !_byId.TryGetValue(id, out var ally) || !ally.Alive || ally.Faction != actor.Faction) return;
        if (Distance(actor, ally) > range) return;
        var def = ShipClassDefinition.Get(ally.Class);
        var before = ally.Hull + ally.ShieldOrCarapace;
        ally.Hull = Math.Min(def.Hull, ally.Hull + amount * 0.70f);
        ally.ShieldOrCarapace = Math.Min(def.ShieldOrCarapace, ally.ShieldOrCarapace + amount * 0.30f);
        if (ally.Hull + ally.ShieldOrCarapace > before)
            _metrics.RepairEvents++;
    }

    private void Regenerate(ShipState actor, ShipClassDefinition def)
    {
        var before = actor.Hull + actor.ShieldOrCarapace;
        actor.Hull = Math.Min(def.Hull, actor.Hull + Math.Max(4f, def.RepairAmount));
        actor.ShieldOrCarapace = Math.Min(def.ShieldOrCarapace, actor.ShieldOrCarapace + 2f);
        if (actor.Hull + actor.ShieldOrCarapace > before)
            _metrics.RepairEvents++;
    }

    private int? ChooseTargetForAction(ShipState ship, ShipActionType type) => type switch
    {
        ShipActionType.FocusFire or ShipActionType.LaunchDrone => ValidEnemyId(_agents[ship.Id].Bb.GetOrDefault(BenchmarkBlackboardKeys.FocusTargetId, -1), ship.Faction)
            ?? ValidEnemyId(_agents[ship.Id].Bb.GetOrDefault(BenchmarkBlackboardKeys.NearestVisibleEnemyId, -1), ship.Faction),
        ShipActionType.RepairAlly => ValidAllyId(_agents[ship.Id].Bb.GetOrDefault(BenchmarkBlackboardKeys.VulnerableAllyId, -1), ship.Faction),
        _ => _agents[ship.Id].Bb.GetOrDefault(BenchmarkBlackboardKeys.NearestVisibleEnemyId, -1) > 0
            ? _agents[ship.Id].Bb.GetOrDefault(BenchmarkBlackboardKeys.NearestVisibleEnemyId, -1)
            : null
    };

    private int? ValidEnemyId(int id, Faction faction) => _byId.TryGetValue(id, out var ship) && ship.Alive && ship.Faction != faction ? id : null;
    private int? ValidAllyId(int id, Faction faction) => _byId.TryGetValue(id, out var ship) && ship.Alive && ship.Faction == faction ? id : null;

    private ShipState? FindNearestEnemy(ShipState ship, float maxDistance)
    {
        ShipState? best = null;
        var bestDist = maxDistance * maxDistance;
        foreach (var other in _ships)
        {
            if (!other.Alive || other.Faction == ship.Faction) continue;
            var d = DistanceSquared(ship, other);
            if (d < bestDist || (Math.Abs(d - bestDist) < 0.0001f && other.Id < (best?.Id ?? int.MaxValue)))
            {
                bestDist = d;
                best = other;
            }
        }
        return best;
    }

    private ShipState? FindVulnerableAlly(ShipState ship, float maxDistance)
    {
        ShipState? best = null;
        var bestHealth = 0.78f;
        foreach (var other in _ships)
        {
            if (!other.Alive || other.Faction != ship.Faction) continue;
            if (Distance(ship, other) > maxDistance) continue;
            var def = ShipClassDefinition.Get(other.Class);
            var health = other.Hull / def.Hull;
            if (health < bestHealth || (Math.Abs(health - bestHealth) < 0.0001f && other.Id < (best?.Id ?? int.MaxValue)))
            {
                bestHealth = health;
                best = other;
            }
        }
        return best;
    }

    private float ComputeThreat(ShipState ship, ShipState? nearest)
    {
        if (nearest is null) return 0f;
        var dist = Distance(ship, nearest);
        var enemyDef = ShipClassDefinition.Get(nearest.Class);
        var rangePressure = dist <= enemyDef.Range ? 0.65f : Math.Max(0f, 0.35f - dist / 220f);
        return Math.Clamp(rangePressure + enemyDef.Damage / 100f, 0f, 1f);
    }

    private static int PriorityFor(ShipActionType type) => type switch
    {
        ShipActionType.Retreat => 0,
        ShipActionType.ScreenHighValue or ShipActionType.HoldFormation or ShipActionType.Advance => 1,
        ShipActionType.RepairAlly or ShipActionType.Regenerate => 2,
        ShipActionType.FocusFire or ShipActionType.LaunchDrone => 3,
        _ => 4
    };

    private void MoveToward(ShipState ship, ShipState? target, float amount)
    {
        if (target is null)
        {
            ship.X += ship.Faction == Faction.Dominion ? amount : -amount;
            return;
        }
        MoveVector(ship, target.X - ship.X, target.Y - ship.Y, amount);
    }

    private static void MoveAway(ShipState ship, ShipState? target, float amount)
    {
        if (target is null) return;
        MoveVector(ship, ship.X - target.X, ship.Y - target.Y, amount);
    }

    private static void Hold(ShipState ship, float amount)
    {
        ship.X += ship.Faction == Faction.Dominion ? amount : -amount;
        ship.Y *= 0.999f;
    }

    private static void MoveVector(ShipState ship, float dx, float dy, float amount)
    {
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len <= 0.0001f) return;
        ship.X += dx / len * amount;
        ship.Y += dy / len * amount;
    }

    private static float Distance(ShipState a, ShipState b) => MathF.Sqrt(DistanceSquared(a, b));
    private static float DistanceSquared(ShipState a, ShipState b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    public (float Dominion, float Collective) ComputeFleetPower()
    {
        float d = 0, c = 0;
        foreach (var ship in _ships)
        {
            if (!ship.Alive) continue;
            var def = ShipClassDefinition.Get(ship.Class);
            var power = def.RoleWeight * (0.55f * Math.Clamp(ship.Hull / def.Hull, 0f, 1f)
                + 0.25f * Math.Clamp(ship.ShieldOrCarapace / Math.Max(1f, def.ShieldOrCarapace), 0f, 1f)
                + 0.20f);
            if (ship.Faction == Faction.Dominion) d += power; else c += power;
        }
        return (d, c);
    }

    private void WriteCheckpoint(int tick)
    {
        var power = ComputeFleetPower();
        var initial = InitialPowerByFaction();
        var dPct = initial.Dominion <= 0 ? 0 : power.Dominion / initial.Dominion * 100f;
        var cPct = initial.Collective <= 0 ? 0 : power.Collective / initial.Collective * 100f;
        var dDestroyed = _ships.Count(s => s.Faction == Faction.Dominion && !s.Alive);
        var cDestroyed = _ships.Count(s => s.Faction == Faction.Collective && !s.Alive);
        var line = $"[T+{tick:0000}] Dominion {dPct:0}% fleet power | Collective {cPct:0}% fleet power | destroyed D:{dDestroyed} C:{cDestroyed} | decisions {_metrics.DecisionsEvaluated} | actions {_metrics.ActionsEmitted} | events {_metrics.EventsDelivered}";
        _checkpoints.Add(line);
        _output?.WriteLine(line);
    }

    private (float Dominion, float Collective) InitialPowerByFaction()
    {
        float d = 0, c = 0;
        foreach (var ship in _ships)
        {
            var power = ShipClassDefinition.Get(ship.Class).RoleWeight;
            if (ship.Faction == Faction.Dominion) d += power; else c += power;
        }
        return (d, c);
    }

    private static Faction Opposing(Faction faction) => faction == Faction.Dominion ? Faction.Collective : Faction.Dominion;
}
