using System.Diagnostics;
using Dominatus.Core.Blackboard;
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
    private ShipAction[] _sortedActions = [];
    private readonly BenchmarkMetrics _metrics = new();
    private readonly List<string> _checkpoints = new();
    private readonly TextWriter? _output;
    private readonly bool _writeCheckpoints;
    private readonly int _checkpointInterval;
    private readonly int _initialShips;
    private readonly RtsSensorMode _sensorMode;
    private readonly float _spatialCellSize;
    private readonly bool _dynamicSensorCadenceEnabled;
    private readonly int _minSensorCadenceTicks;
    private readonly int _maxSensorCadenceTicks;
    private readonly Dictionary<int, SensorCadenceState> _sensorCadence = new();
    private readonly SpatialShipGrid? _spatialGrid;
    private readonly List<ShipState> _spatialCandidates = new();
    private readonly RtsAgentExecutionMode _agentExecutionMode;
    private readonly int _maxDegreeOfParallelism;
    private readonly ParallelOptions _parallelDecisionOptions;
    private readonly ParallelAiWorldRunner _coreParallelRunner = new();

    public BattleSimulation(int shipCount, int checkpointInterval, bool writeCheckpoints, TextWriter? output, RtsSensorMode sensorMode, float spatialCellSize)
        : this(shipCount, checkpointInterval, writeCheckpoints, output, sensorMode, spatialCellSize, enableDynamicSensorCadence: false, minSensorCadenceTicks: 1, maxSensorCadenceTicks: 12, agentExecutionMode: RtsAgentExecutionMode.Sequential, maxDegreeOfParallelism: Environment.ProcessorCount)
    {
    }

    public BattleSimulation(
        int shipCount,
        int checkpointInterval,
        bool writeCheckpoints,
        TextWriter? output,
        RtsSensorMode sensorMode,
        float spatialCellSize,
        bool enableDynamicSensorCadence,
        int minSensorCadenceTicks,
        int maxSensorCadenceTicks,
        RtsAgentExecutionMode agentExecutionMode = RtsAgentExecutionMode.Sequential,
        int maxDegreeOfParallelism = 1)
        : this(
            FleetFactory.CreateFleets(shipCount),
            checkpointInterval,
            writeCheckpoints,
            output,
            sensorMode,
            spatialCellSize,
            enableDynamicSensorCadence,
            minSensorCadenceTicks,
            maxSensorCadenceTicks,
            agentExecutionMode,
            maxDegreeOfParallelism,
            metricsCheckpoint: null,
            checkpointLines: null,
            sensorCadenceCheckpoints: null)
    {
    }

    public BattleSimulation(
        RtsBenchmarkCheckpoint checkpoint,
        int checkpointInterval,
        bool writeCheckpoints,
        TextWriter? output,
        RtsSensorMode sensorMode,
        float spatialCellSize,
        bool enableDynamicSensorCadence,
        int minSensorCadenceTicks,
        int maxSensorCadenceTicks,
        RtsAgentExecutionMode agentExecutionMode = RtsAgentExecutionMode.Sequential,
        int maxDegreeOfParallelism = 1)
        : this(
            checkpoint.Ships.Select(ToShipState).ToList(),
            checkpointInterval,
            writeCheckpoints,
            output,
            sensorMode,
            spatialCellSize,
            enableDynamicSensorCadence,
            minSensorCadenceTicks,
            maxSensorCadenceTicks,
            agentExecutionMode,
            maxDegreeOfParallelism,
            checkpoint.Metrics,
            checkpoint.Checkpoints,
            checkpoint.Ships)
    {
    }

    private BattleSimulation(
        List<ShipState> ships,
        int checkpointInterval,
        bool writeCheckpoints,
        TextWriter? output,
        RtsSensorMode sensorMode,
        float spatialCellSize,
        bool enableDynamicSensorCadence,
        int minSensorCadenceTicks,
        int maxSensorCadenceTicks,
        RtsAgentExecutionMode agentExecutionMode,
        int maxDegreeOfParallelism,
        RtsBenchmarkMetricsCheckpoint? metricsCheckpoint,
        IReadOnlyList<string>? checkpointLines,
        IReadOnlyList<ShipCheckpoint>? sensorCadenceCheckpoints)
    {
        _sensorMode = sensorMode;
        _spatialCellSize = spatialCellSize;
        _dynamicSensorCadenceEnabled = enableDynamicSensorCadence;
        _minSensorCadenceTicks = minSensorCadenceTicks;
        _maxSensorCadenceTicks = maxSensorCadenceTicks;
        _agentExecutionMode = agentExecutionMode;
        _maxDegreeOfParallelism = Math.Max(1, maxDegreeOfParallelism);
        _parallelDecisionOptions = new ParallelOptions { MaxDegreeOfParallelism = _maxDegreeOfParallelism };
        _ships = ships;
        _byId = _ships.ToDictionary(s => s.Id);
        _initialShips = _ships.Count;
        _checkpointInterval = checkpointInterval;
        _writeCheckpoints = writeCheckpoints;
        _output = output;

        var sensorByShip = sensorCadenceCheckpoints?.ToDictionary(s => s.Id);
        foreach (var ship in _ships)
        {
            var agent = ShipAgentFactory.Create(ship);
            _world.Add(agent);
            _agents[ship.Id] = agent;
            _shipToAgentId[ship.Id] = agent.Id.Value;

            ShipCheckpoint? shipCheckpoint = null;
            if (sensorByShip is not null)
                sensorByShip.TryGetValue(ship.Id, out shipCheckpoint);

            var cadence = CreateSensorCadenceState(ship, shipCheckpoint);
            _sensorCadence[ship.Id] = cadence;
            RestoreAgentBlackboard(agent, ship, cadence, shipCheckpoint?.FocusTargetId ?? -1);
            if (shipCheckpoint?.ActiveStatePath is { Count: > 0 } activePath)
                agent.Brain.RestoreActivePath(_world, agent, activePath.ToArray());
        }

        if (metricsCheckpoint is not null)
            _metrics.RestoreDeterministicCheckpoint(metricsCheckpoint);
        if (checkpointLines is not null)
            _checkpoints.AddRange(checkpointLines);

        if (_sensorMode == RtsSensorMode.SpatialGrid)
            _spatialGrid = new SpatialShipGrid(_spatialCellSize, _ships);
    }

    public IReadOnlyList<ShipState> Ships => _ships;
    public BenchmarkMetrics Metrics => _metrics;
    public IReadOnlyList<string> Checkpoints => _checkpoints;

    public void RunTicks(int ticks, int completedTicks = 0)
    {
        if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
        for (var tick = completedTicks + 1; tick <= completedTicks + ticks; tick++)
            RunTick(tick);
    }

    public RtsBenchmarkCheckpoint CreateCheckpoint(RtsBenchmarkOptions options, int completedTicks)
    {
        if (completedTicks < 0) throw new ArgumentOutOfRangeException(nameof(completedTicks));
        return new RtsBenchmarkCheckpoint
        {
            Options = options,
            CompletedTicks = completedTicks,
            Ships = _ships.Select(CreateShipCheckpoint).OrderBy(s => s.Id).ToArray(),
            Metrics = _metrics.CaptureDeterministicCheckpoint(),
            Checkpoints = _checkpoints.ToArray(),
            Diagnostics = new RtsBenchmarkDiagnosticsCheckpoint
            {
                SensorMode = _sensorMode,
                SpatialCellSize = _spatialCellSize,
                DynamicSensorCadenceEnabled = _dynamicSensorCadenceEnabled,
                MinSensorCadenceTicks = _minSensorCadenceTicks,
                MaxSensorCadenceTicks = _maxSensorCadenceTicks,
                Boundary = "tick-complete/action-buffer-cleared/event-delivery-complete"
            }
        };
    }

    public int MinSensorCadenceTicks => _minSensorCadenceTicks;
    public int MaxSensorCadenceTicks => _maxSensorCadenceTicks;
    public RtsAgentExecutionMode AgentExecutionMode => _agentExecutionMode;
    public bool ParallelAgents => _agentExecutionMode != RtsAgentExecutionMode.Sequential;
    public int MaxDegreeOfParallelism => _maxDegreeOfParallelism;

    public void RunTick(int tick)
    {
        _actions.Clear();

        var phaseStart = Stopwatch.GetTimestamp();
        DecrementCooldowns();
        _metrics.AddPhaseTicks(BenchmarkMetrics.CooldownPhase, Stopwatch.GetTimestamp() - phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        SensorPhase(tick);
        _metrics.AddPhaseTicks(BenchmarkMetrics.SensorPhase, Stopwatch.GetTimestamp() - phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        DecisionPhase(tick);
        _metrics.AddPhaseTicks(BenchmarkMetrics.DecisionPhase, Stopwatch.GetTimestamp() - phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        SortActions();
        _metrics.AddPhaseTicks(BenchmarkMetrics.ActionSortPhase, Stopwatch.GetTimestamp() - phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        ResolutionPhase(tick);
        _metrics.AddPhaseTicks(BenchmarkMetrics.ActionResolutionPhase, Stopwatch.GetTimestamp() - phaseStart);

        phaseStart = Stopwatch.GetTimestamp();
        EventPhase(tick);
        _metrics.AddPhaseTicks(BenchmarkMetrics.EventDeliveryPhase, Stopwatch.GetTimestamp() - phaseStart);

        if (_writeCheckpoints && tick % _checkpointInterval == 0)
        {
            phaseStart = Stopwatch.GetTimestamp();
            WriteCheckpoint(tick);
            _metrics.AddPhaseTicks(BenchmarkMetrics.CheckpointPhase, Stopwatch.GetTimestamp() - phaseStart);
        }
    }

    private static ShipState ToShipState(ShipCheckpoint checkpoint) => new()
    {
        Id = checkpoint.Id,
        Faction = checkpoint.Faction,
        Class = checkpoint.Class,
        X = checkpoint.X,
        Y = checkpoint.Y,
        Hull = checkpoint.Hull,
        ShieldOrCarapace = checkpoint.ShieldOrCarapace,
        CooldownRemaining = checkpoint.CooldownRemaining,
        Alive = checkpoint.Alive,
        TargetId = checkpoint.TargetId,
        CurrentAction = checkpoint.CurrentAction
    };

    private ShipCheckpoint CreateShipCheckpoint(ShipState ship)
    {
        var cadence = _sensorCadence[ship.Id];
        return new ShipCheckpoint
        {
            Id = ship.Id,
            Faction = ship.Faction,
            Class = ship.Class,
            X = ship.X,
            Y = ship.Y,
            Hull = ship.Hull,
            ShieldOrCarapace = ship.ShieldOrCarapace,
            CooldownRemaining = ship.CooldownRemaining,
            Alive = ship.Alive,
            TargetId = ship.TargetId,
            CurrentAction = ship.CurrentAction,
            FocusTargetId = _agents.TryGetValue(ship.Id, out var agent) ? agent.Bb.GetOrDefault(BenchmarkBlackboardKeys.FocusTargetId, -1) : -1,
            ActiveStatePath = _agents.TryGetValue(ship.Id, out agent) ? agent.Brain.GetActivePath().Select(state => state.ToString()).ToArray() : [],
            LastTacticalSummary = cadence.LastTacticalSummary is null ? null : ToCheckpoint(cadence.LastTacticalSummary),
            NextSensorRefreshTick = cadence.NextSensorRefreshTick,
            LastSensorRefreshTick = cadence.LastSensorRefreshTick,
            CurrentSensorCadenceTicks = cadence.CurrentSensorCadenceTicks,
            ForceSensorRefresh = cadence.ForceSensorRefresh,
            LastDamageIntegrity = cadence.LastObservedIntegrity
        };
    }

    private static TacticalSummaryCheckpoint ToCheckpoint(TacticalSummary summary) => new()
    {
        ImmediateThreatId = summary.ImmediateThreatId,
        BestAttackTargetId = summary.BestAttackTargetId,
        BestRepairTargetId = summary.BestRepairTargetId,
        HighestValueVisibleEnemyId = summary.HighestValueVisibleEnemyId,
        LocalThreatScore = summary.LocalThreatScore,
        LocalSupportScore = summary.LocalSupportScore,
        RelevantEnemyContacts = summary.RelevantEnemyContacts,
        RelevantAllyContacts = summary.RelevantAllyContacts,
        BestAttackTargetBand = summary.BestAttackTargetBand,
        BestAttackPriorityScore = summary.BestAttackPriorityScore
    };

    private static TacticalSummary ToTacticalSummary(TacticalSummaryCheckpoint checkpoint) => new()
    {
        ImmediateThreatId = checkpoint.ImmediateThreatId,
        BestAttackTargetId = checkpoint.BestAttackTargetId,
        BestRepairTargetId = checkpoint.BestRepairTargetId,
        HighestValueVisibleEnemyId = checkpoint.HighestValueVisibleEnemyId,
        LocalThreatScore = checkpoint.LocalThreatScore,
        LocalSupportScore = checkpoint.LocalSupportScore,
        RelevantEnemyContacts = checkpoint.RelevantEnemyContacts,
        RelevantAllyContacts = checkpoint.RelevantAllyContacts,
        BestAttackTargetBand = checkpoint.BestAttackTargetBand,
        BestAttackPriorityScore = checkpoint.BestAttackPriorityScore
    };

    private SensorCadenceState CreateSensorCadenceState(ShipState ship, ShipCheckpoint? checkpoint)
    {
        if (checkpoint is null)
        {
            return new SensorCadenceState
            {
                CurrentSensorCadenceTicks = _minSensorCadenceTicks,
                LastObservedIntegrity = ship.Hull + ship.ShieldOrCarapace
            };
        }

        return new SensorCadenceState
        {
            NextSensorRefreshTick = checkpoint.NextSensorRefreshTick,
            LastSensorRefreshTick = checkpoint.LastSensorRefreshTick,
            CurrentSensorCadenceTicks = checkpoint.CurrentSensorCadenceTicks,
            ForceSensorRefresh = checkpoint.ForceSensorRefresh,
            LastTacticalSummary = checkpoint.LastTacticalSummary is null ? null : ToTacticalSummary(checkpoint.LastTacticalSummary),
            LastObservedIntegrity = checkpoint.LastDamageIntegrity
        };
    }

    private static void RestoreAgentBlackboard(AiAgent agent, ShipState ship, SensorCadenceState cadence, int focusTargetId)
    {
        var def = ShipClassDefinition.Get(ship.Class);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.CurrentAction.Name, ship.CurrentAction);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.FocusTargetId.Name, focusTargetId);
        if (cadence.LastTacticalSummary is { } summary)
        {
            var ownHullFraction = Math.Clamp(ship.Hull / Math.Max(1f, def.Hull), 0f, 1f);
            SetTacticalSummaryRaw(agent, ownHullFraction, ship.CooldownRemaining <= 0, summary);
        }
    }

    private static void SetTacticalSummaryRaw(AiAgent agent, float ownHullFraction, bool cooldownReady, TacticalSummary summary)
    {
        var immediateThreatId = summary.ImmediateThreatId ?? -1;
        var bestAttackTargetId = summary.BestAttackTargetId ?? -1;
        var bestRepairTargetId = summary.BestRepairTargetId ?? -1;
        var highestValueVisibleEnemyId = summary.HighestValueVisibleEnemyId ?? -1;
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.OwnHullFraction.Name, ownHullFraction);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.ThreatScore.Name, summary.LocalThreatScore);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.LocalThreatScore.Name, summary.LocalThreatScore);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.LocalSupportScore.Name, summary.LocalSupportScore);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.ImmediateThreatId.Name, immediateThreatId);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.BestAttackTargetId.Name, bestAttackTargetId);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.BestRepairTargetId.Name, bestRepairTargetId);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.HighestValueVisibleEnemyId.Name, highestValueVisibleEnemyId);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.NearestVisibleEnemyId.Name, bestAttackTargetId);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.VulnerableAllyId.Name, bestRepairTargetId);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.TargetValue.Name, summary.BestAttackPriorityScore);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.RelevantEnemyContacts.Name, summary.RelevantEnemyContacts);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.RelevantAllyContacts.Name, summary.RelevantAllyContacts);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.HasImmediateThreat.Name, immediateThreatId > 0);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.HasRepairTarget.Name, bestRepairTargetId > 0);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.BestAttackTargetBand.Name, (int)summary.BestAttackTargetBand);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.BestAttackPriorityScore.Name, summary.BestAttackPriorityScore);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.CooldownReady.Name, cooldownReady);
        agent.Bb.SetRaw(BenchmarkBlackboardKeys.EnemyInWeaponRange.Name, summary.BestAttackTargetBand == TacticalDistanceBand.Immediate);
    }

    private void DecrementCooldowns()
    {
        foreach (var ship in _ships)
        {
            if (ship.Alive && ship.CooldownRemaining > 0)
                ship.CooldownRemaining--;
        }
    }

    private void SensorPhase(int tick)
    {
        var aliveShips = 0;
        foreach (var candidate in _ships)
        {
            if (candidate.Alive) aliveShips++;
        }

        if (_sensorMode == RtsSensorMode.SpatialGrid)
        {
            _spatialGrid!.Rebuild(_ships);
            _metrics.SpatialMaxCellsUsed = Math.Max(_metrics.SpatialMaxCellsUsed, _spatialGrid.PopulatedCells);
        }

        foreach (var ship in _ships)
        {
            if (!ship.Alive) continue;
            var agent = _agents[ship.Id];
            var state = _sensorCadence[ship.Id];
            var def = ShipClassDefinition.Get(ship.Class);
            var ownHullFraction = Math.Clamp(ship.Hull / Math.Max(1f, def.Hull), 0f, 1f);

            var damageInvalidated = TookDamageSinceLastRefresh(ship, state);
            var targetInvalidated = CurrentTargetInvalid(ship);
            var dueByCadence = tick >= state.NextSensorRefreshTick;
            var missingSummary = state.LastTacticalSummary is null;
            var forcedByEvent = state.ForceSensorRefresh;
            var shouldRefresh = !_dynamicSensorCadenceEnabled || dueByCadence || forcedByEvent || missingSummary || damageInvalidated || targetInvalidated;

            if (!shouldRefresh)
            {
                _metrics.SensorRefreshesSkipped++;
                _metrics.StaleTacticalSummaryUses++;
                MirrorTacticalSummary(agent, ownHullFraction, ship.CooldownRemaining <= 0, state.LastTacticalSummary!);
                continue;
            }

            if (_dynamicSensorCadenceEnabled)
            {
                if (forcedByEvent || damageInvalidated || targetInvalidated)
                    _metrics.ForcedSensorRefreshes++;
                if (forcedByEvent) _metrics.EventForcedRefreshes++;
                if (damageInvalidated) _metrics.DamageForcedRefreshes++;
                if (targetInvalidated) _metrics.TargetInvalidationRefreshes++;
            }

            _metrics.SensorRefreshesPerformed++;
            _metrics.BroadSensorPairsEquivalent += Math.Max(0, aliveShips - 1);
            var focusTargetId = BbGet(agent, BenchmarkBlackboardKeys.FocusTargetId, -1);
            var counters = new TacticalContactCounters();
            var contacts = GetSensorCandidates(ship, def);
            var summary = TacticalModel.ComputeSummary(ship, contacts, focusTargetId, ref counters);
            AddTacticalCounters(counters);
            MirrorTacticalSummary(agent, ownHullFraction, ship.CooldownRemaining <= 0, summary);

            var cadence = _dynamicSensorCadenceEnabled
                ? SelectSensorCadence(ship, def, ownHullFraction, summary)
                : _minSensorCadenceTicks;
            if (!_dynamicSensorCadenceEnabled)
                _metrics.TotalSelectedSensorCadenceTicks += cadence;
            state.LastTacticalSummary = summary;
            state.LastSensorRefreshTick = tick;
            state.CurrentSensorCadenceTicks = cadence;
            state.NextSensorRefreshTick = tick + cadence;
            state.ForceSensorRefresh = false;
            state.LastObservedIntegrity = ship.Hull + ship.ShieldOrCarapace;
        }
    }

    private bool TookDamageSinceLastRefresh(ShipState ship, SensorCadenceState state)
    {
        var integrity = ship.Hull + ship.ShieldOrCarapace;
        return integrity < state.LastObservedIntegrity - 0.001f;
    }

    private bool CurrentTargetInvalid(ShipState ship)
    {
        return ship.TargetId is int targetId && targetId > 0 && (!_byId.TryGetValue(targetId, out var target) || !target.Alive);
    }

    internal int SelectSensorCadenceForTest(ShipState ship, TacticalSummary summary)
    {
        var def = ShipClassDefinition.Get(ship.Class);
        var ownHullFraction = Math.Clamp(ship.Hull / Math.Max(1f, def.Hull), 0f, 1f);
        return SelectSensorCadence(ship, def, ownHullFraction, summary);
    }

    private int SelectSensorCadence(ShipState ship, ShipClassDefinition def, float ownHullFraction, TacticalSummary summary)
    {
        int cadence;
        if (summary.ImmediateThreatId is > 0)
        {
            cadence = 1;
            _metrics.ImmediateCadenceSelections++;
        }
        else if (summary.BestAttackTargetBand == TacticalDistanceBand.Near || summary.LocalThreatScore >= 0.35f)
        {
            cadence = 2;
            _metrics.NearCadenceSelections++;
        }
        else if (summary.BestAttackTargetBand == TacticalDistanceBand.Sensor || summary.RelevantEnemyContacts > 0 || summary.RelevantAllyContacts > 0)
        {
            cadence = 4;
            _metrics.SensorBandCadenceSelections++;
        }
        else
        {
            cadence = 8;
            _metrics.IdleCadenceSelections++;
        }

        cadence = ApplyRoleCadence(ship, summary, cadence);

        if (ownHullFraction < 0.35f)
            cadence = Math.Min(cadence, 2);
        if (ship.TargetId is int targetId && targetId > 0 && _byId.TryGetValue(targetId, out var target) && target.Alive)
            cadence = Math.Min(cadence, 2);

        cadence = Math.Clamp(cadence, _minSensorCadenceTicks, _maxSensorCadenceTicks);
        _metrics.TotalSelectedSensorCadenceTicks += cadence;
        return cadence;
    }

    private static int ApplyRoleCadence(ShipState ship, TacticalSummary summary, int cadence)
    {
        switch (ship.Class)
        {
            case ShipClass.ScoutFrigate:
            case ShipClass.CommandCruiser:
            case ShipClass.SynapseCruiser:
                return Math.Min(cadence, 3);
            case ShipClass.Carrier:
            case ShipClass.HiveArk:
                return Math.Min(cadence, 4);
            case ShipClass.RepairTender:
            case ShipClass.Regenerator:
                return summary.BestRepairTargetId is > 0 ? Math.Min(cadence, 3) : Math.Min(cadence, 4);
            case ShipClass.NeedleDrone:
                return summary.ImmediateThreatId is > 0 || summary.BestAttackTargetBand == TacticalDistanceBand.Immediate
                    ? Math.Min(cadence, 2)
                    : Math.Min(cadence, 4);
            default:
                return cadence;
        }
    }

    private IEnumerable<ShipState> GetSensorCandidates(ShipState ship, ShipClassDefinition def)
    {
        if (_sensorMode == RtsSensorMode.BroadScan)
        {
            return _ships;
        }

        var candidateIds = _spatialGrid!.QueryCandidateIds(ship.X, ship.Y, def.SensorRange, out var cellsVisited);
        _metrics.SpatialCellQueries += cellsVisited;
        _spatialCandidates.Clear();
        foreach (var id in candidateIds)
        {
            if (id == ship.Id) continue;
            _spatialCandidates.Add(_spatialGrid.ShipById(id));
        }

        _metrics.SpatialCandidatePairs += _spatialCandidates.Count;
        return _spatialCandidates;
    }

    private void AddTacticalCounters(TacticalContactCounters counters)
    {
        _metrics.SensorPairsChecked += counters.SensorPairsChecked;
        _metrics.RelevantEnemyContacts += counters.RelevantEnemyContacts;
        _metrics.RelevantAllyContacts += counters.RelevantAllyContacts;
        _metrics.IgnoredOutOfRangeContacts += counters.IgnoredOutOfRangeContacts;
        _metrics.ImmediateThreatContacts += counters.ImmediateThreatContacts;
        _metrics.NearContacts += counters.NearContacts;
        _metrics.SensorBandContacts += counters.SensorBandContacts;
    }

    private void MirrorTacticalSummary(AiAgent agent, float ownHullFraction, bool cooldownReady, TacticalSummary summary)
    {
        var immediateThreatId = summary.ImmediateThreatId ?? -1;
        var bestAttackTargetId = summary.BestAttackTargetId ?? -1;
        var bestRepairTargetId = summary.BestRepairTargetId ?? -1;
        var highestValueVisibleEnemyId = summary.HighestValueVisibleEnemyId ?? -1;

        BbSet(agent, BenchmarkBlackboardKeys.OwnHullFraction, ownHullFraction);
        BbSet(agent, BenchmarkBlackboardKeys.ThreatScore, summary.LocalThreatScore);
        BbSet(agent, BenchmarkBlackboardKeys.LocalThreatScore, summary.LocalThreatScore);
        BbSet(agent, BenchmarkBlackboardKeys.LocalSupportScore, summary.LocalSupportScore);
        BbSet(agent, BenchmarkBlackboardKeys.ImmediateThreatId, immediateThreatId);
        BbSet(agent, BenchmarkBlackboardKeys.BestAttackTargetId, bestAttackTargetId);
        BbSet(agent, BenchmarkBlackboardKeys.BestRepairTargetId, bestRepairTargetId);
        BbSet(agent, BenchmarkBlackboardKeys.HighestValueVisibleEnemyId, highestValueVisibleEnemyId);
        BbSet(agent, BenchmarkBlackboardKeys.NearestVisibleEnemyId, bestAttackTargetId);
        BbSet(agent, BenchmarkBlackboardKeys.VulnerableAllyId, bestRepairTargetId);
        BbSet(agent, BenchmarkBlackboardKeys.TargetValue, summary.BestAttackPriorityScore);
        BbSet(agent, BenchmarkBlackboardKeys.RelevantEnemyContacts, summary.RelevantEnemyContacts);
        BbSet(agent, BenchmarkBlackboardKeys.RelevantAllyContacts, summary.RelevantAllyContacts);
        BbSet(agent, BenchmarkBlackboardKeys.HasImmediateThreat, immediateThreatId > 0);
        BbSet(agent, BenchmarkBlackboardKeys.HasRepairTarget, bestRepairTargetId > 0);
        BbSet(agent, BenchmarkBlackboardKeys.BestAttackTargetBand, (int)summary.BestAttackTargetBand);
        BbSet(agent, BenchmarkBlackboardKeys.BestAttackPriorityScore, summary.BestAttackPriorityScore);
        BbSet(agent, BenchmarkBlackboardKeys.CooldownReady, cooldownReady);
        BbSet(agent, BenchmarkBlackboardKeys.EnemyInWeaponRange, summary.BestAttackTargetBand == TacticalDistanceBand.Immediate);
    }

    private void DecisionPhase(int tick)
    {
        switch (_agentExecutionMode)
        {
            case RtsAgentExecutionMode.LocalParallelDecision:
                ParallelDecisionPhase(tick);
                return;
            case RtsAgentExecutionMode.CoreParallelRunner:
                CoreParallelDecisionPhase(tick);
                return;
        }

        foreach (var ship in _ships)
        {
            if (!ship.Alive) continue;
            var result = TickAgentForDecision(ship);
            AddDecisionWorkMetrics(result, parallel: false);
            StageAction(tick, ship, result.Type);
        }
    }

    private void ParallelDecisionPhase(int tick)
    {
        var workItems = _ships
            .Where(ship => ship.Alive)
            .OrderBy(ship => ship.Id)
            .ToArray();
        var results = new DecisionWorkResult[workItems.Length];

        if (workItems.Length == 0)
            return;

        _metrics.ParallelDecisionTasksScheduled += workItems.Length;
        try
        {
            Parallel.For(0, workItems.Length, _parallelDecisionOptions, i =>
            {
                results[i] = TickAgentForDecision(workItems[i]);
            });
        }
        catch
        {
            _metrics.ParallelDecisionFaults++;
            throw;
        }

        for (var i = 0; i < workItems.Length; i++)
        {
            var result = results[i];
            AddDecisionWorkMetrics(result, parallel: true);
            StageAction(tick, workItems[i], result.Type);
            _metrics.ParallelLocalActionsStaged++;
        }
    }

    private void CoreParallelDecisionPhase(int tick)
    {
        var workItems = _ships
            .Where(ship => ship.Alive)
            .OrderBy(ship => ship.Id)
            .ToArray();

        if (workItems.Length == 0)
            return;

        var aliveAgentIds = workItems
            .Select(ship => _shipToAgentId[ship.Id])
            .ToHashSet();
        var decisionReads = new System.Collections.Concurrent.ConcurrentQueue<long>();

        _metrics.ParallelDecisionTasksScheduled += workItems.Length;
        ParallelTickResult result;
        try
        {
            result = _coreParallelRunner.Tick(_world, 0f, new ParallelTickOptions
            {
                MaxDegreeOfParallelism = _maxDegreeOfParallelism,
                AdvanceWorldClock = false,
                ExpireWorldBlackboard = false,
                TickActuator = false,
                AgentFilter = agent => aliveAgentIds.Contains(agent.Id.Value),
                AgentTickStarting = _ => UtilityDiagnostics.BeginDecision(),
                AgentTickCompleted = _ => decisionReads.Enqueue(UtilityDiagnostics.EndDecision())
            });
        }
        catch
        {
            _metrics.ParallelDecisionFaults++;
            throw;
        }

        _metrics.CoreParallelAgentsTicked += result.AgentsTicked;
        _metrics.CoreParallelWorldWritesStaged += result.WorldWritesStaged;
        _metrics.CoreParallelMailboxMessagesStaged += result.MailboxMessagesStaged;
        _metrics.CoreParallelActuationsStaged += result.ActuationsStaged;
        _metrics.CoreParallelConflicts += result.Conflicts.Count;

        if (result.WorldWritesStaged != 0 || result.MailboxMessagesStaged != 0 || result.ActuationsStaged != 0 || result.Conflicts.Count != 0)
            throw new InvalidOperationException("Core parallel RTSBenchmark decision phase staged shared effects; the RTS safe subset was violated.");

        var totalDecisionReads = decisionReads.Sum() + workItems.Length;
        for (var i = 0; i < workItems.Length; i++)
        {
            var ship = workItems[i];
            var agent = _agents[ship.Id];
            var selected = agent.Bb.GetOrDefault(BenchmarkBlackboardKeys.CurrentAction, nameof(ShipActionType.Idle));
            var type = Enum.TryParse<ShipActionType>(selected, out var parsed) ? parsed : ShipActionType.Idle;
            var reads = totalDecisionReads / workItems.Length + (i < totalDecisionReads % workItems.Length ? 1 : 0);
            AddDecisionWorkMetrics(new DecisionWorkResult(ship.Id, type, reads), parallel: true);
            StageAction(tick, ship, type);
        }
    }

    private DecisionWorkResult TickAgentForDecision(ShipState ship)
    {
        var agent = _agents[ship.Id];
        long decisionReads;
        UtilityDiagnostics.BeginDecision();
        try
        {
            agent.Tick(_world);
        }
        finally
        {
            decisionReads = UtilityDiagnostics.EndDecision();
        }

        var selected = agent.Bb.GetOrDefault(BenchmarkBlackboardKeys.CurrentAction, nameof(ShipActionType.Idle));
        decisionReads++;
        var type = Enum.TryParse<ShipActionType>(selected, out var parsed) ? parsed : ShipActionType.Idle;
        return new DecisionWorkResult(ship.Id, type, decisionReads);
    }

    private void AddDecisionWorkMetrics(DecisionWorkResult result, bool parallel)
    {
        _metrics.AgentTicks++;
        _metrics.AgentTickCalls++;
        _metrics.HfsmTicks++;
        _metrics.DecideSteps++;
        _metrics.DecisionsEvaluated += ShipAgentFactory.UtilityOptionCount;
        _metrics.UtilityOptionsEvaluated += ShipAgentFactory.UtilityOptionCount;
        _metrics.UtilityOptionsSelected++;
        _metrics.BlackboardReads += result.DecisionBlackboardReads;
        _metrics.DecisionBlackboardReads += result.DecisionBlackboardReads;
        if (parallel)
            _metrics.ParallelAgentTicks++;
    }

    private void StageAction(int tick, ShipState ship, ShipActionType type)
    {
        ship.CurrentAction = type.ToString();
        ship.TargetId = ChooseTargetForAction(ship, type);
        _actions.Add(new ShipAction(tick, ship.Id, ship.Faction, type, ship.TargetId, PriorityFor(type)));
        CountActionEmission(type);
        if (ship.Faction == Faction.Dominion) _metrics.DominionActions++; else _metrics.CollectiveActions++;
    }

    private void SortActions()
    {
        _metrics.ActionSortBatches++;
        _metrics.ActionsSorted += _actions.Count;
        _metrics.MaxActionsInTick = Math.Max(_metrics.MaxActionsInTick, _actions.Count);
        _sortedActions = _actions.OrderBy(a => a.Tick)
            .ThenBy(a => a.Priority)
            .ThenBy(a => a.Faction)
            .ThenBy(a => a.ActorId)
            .ThenBy(a => a.TargetId ?? -1)
            .ThenBy(a => a.Type)
            .ToArray();
    }

    private void ResolutionPhase(int tick)
    {
        foreach (var action in _sortedActions)
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

    private void CountActionEmission(ShipActionType type)
    {
        _metrics.ActionsEmitted++;
        _metrics.ActionStatesEntered++;
        switch (type)
        {
            case ShipActionType.Advance:
                _metrics.AdvanceActionsEmitted++;
                break;
            case ShipActionType.FocusFire:
                _metrics.FocusFireActionsEmitted++;
                break;
            case ShipActionType.Retreat:
                _metrics.RetreatActionsEmitted++;
                break;
            case ShipActionType.RepairAlly:
                _metrics.RepairActionsEmitted++;
                break;
            case ShipActionType.LaunchDrone:
                _metrics.LaunchDroneActionsEmitted++;
                break;
            case ShipActionType.Regenerate:
                _metrics.RegenerateActionsEmitted++;
                break;
            case ShipActionType.ScreenHighValue:
            case ShipActionType.HoldFormation:
                _metrics.HoldFormationActionsEmitted++;
                break;
            case ShipActionType.Idle:
                _metrics.IdleActionsEmitted++;
                break;
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
            _metrics.MailboxEventsSent++;
            CountEventMessage(message);
            if (_world.Mail.Send(new AgentId(_shipToAgentId[ship.Id]), message))
            {
                _metrics.EventsDelivered++;
                _metrics.MailboxEventsDelivered++;
                if (sourceFaction == Faction.Dominion) _metrics.DominionEvents++; else _metrics.CollectiveEvents++;
                if (RequiresSensorRefresh(message, ship))
                    _sensorCadence[ship.Id].ForceSensorRefresh = true;
                if (message is CommandFocusOrder order)
                    BbSetDecision(_agents[ship.Id], BenchmarkBlackboardKeys.FocusTargetId, order.TargetShipId);
            }
        }
    }

    private static bool RequiresSensorRefresh<T>(T message, ShipState recipient) where T : notnull
    {
        return message switch
        {
            TargetSpotted => true,
            AllyUnderFire => true,
            CommandFocusOrder => true,
            SynapseLost => recipient.Faction == Faction.Collective,
            RepairRequested => ShipClassDefinition.Get(recipient.Class).RepairAmount > 0f,
            _ => false
        };
    }

    private void CountEventMessage<T>(T message) where T : notnull
    {
        switch (message)
        {
            case TargetSpotted:
                _metrics.TargetSpottedEvents++;
                break;
            case RepairRequested:
                _metrics.RepairRequestedEvents++;
                break;
            case CommandFocusOrder:
                _metrics.CommandFocusOrderEvents++;
                break;
            case ShipDestroyed:
                _metrics.ShipDestroyedEvents++;
                break;
            case SynapseLost:
                _metrics.SynapseLostEvents++;
                break;
            case AllyUnderFire:
                _metrics.AllyUnderFireEvents++;
                break;
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
        ShipActionType.FocusFire or ShipActionType.LaunchDrone => ValidEnemyId(BbGetDecision(_agents[ship.Id], BenchmarkBlackboardKeys.BestAttackTargetId, -1), ship.Faction)
            ?? ValidEnemyId(BbGetDecision(_agents[ship.Id], BenchmarkBlackboardKeys.FocusTargetId, -1), ship.Faction),
        ShipActionType.RepairAlly => ValidAllyId(BbGetDecision(_agents[ship.Id], BenchmarkBlackboardKeys.BestRepairTargetId, -1), ship.Faction),
        ShipActionType.Retreat => ValidEnemyId(BbGetDecision(_agents[ship.Id], BenchmarkBlackboardKeys.ImmediateThreatId, -1), ship.Faction)
            ?? ValidEnemyId(BbGetDecision(_agents[ship.Id], BenchmarkBlackboardKeys.BestAttackTargetId, -1), ship.Faction),
        _ => ValidEnemyId(BbGetDecision(_agents[ship.Id], BenchmarkBlackboardKeys.BestAttackTargetId, -1), ship.Faction)
    };

    private int? ValidEnemyId(int id, Faction faction) => _byId.TryGetValue(id, out var ship) && ship.Alive && ship.Faction != faction ? id : null;
    private int? ValidAllyId(int id, Faction faction) => _byId.TryGetValue(id, out var ship) && ship.Alive && ship.Faction == faction ? id : null;

    private ShipState? FindNearestEnemy(ShipState ship, float maxDistance, bool countSensorPairs = false)
    {
        ShipState? best = null;
        var bestDist = maxDistance * maxDistance;
        foreach (var other in _ships)
        {
            if (countSensorPairs) _metrics.SensorPairsChecked++;
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

    private ShipState? FindVulnerableAlly(ShipState ship, float maxDistance, bool countSensorPairs = false)
    {
        ShipState? best = null;
        var bestHealth = 0.78f;
        foreach (var other in _ships)
        {
            if (countSensorPairs) _metrics.SensorPairsChecked++;
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
        _metrics.CheckpointsWritten++;
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


    private T BbGet<T>(AiAgent agent, BbKey<T> key, T defaultValue = default!)
    {
        _metrics.BlackboardReads++;
        return agent.Bb.GetOrDefault(key, defaultValue);
    }

    private T BbGetDecision<T>(AiAgent agent, BbKey<T> key, T defaultValue = default!)
    {
        _metrics.BlackboardReads++;
        _metrics.DecisionBlackboardReads++;
        return agent.Bb.GetOrDefault(key, defaultValue);
    }

    private void BbSet<T>(AiAgent agent, BbKey<T> key, T value)
    {
        _metrics.BlackboardWrites++;
        _metrics.SensorBlackboardWrites++;
        agent.Bb.Set(key, value);
    }

    private void BbSetDecision<T>(AiAgent agent, BbKey<T> key, T value)
    {
        _metrics.BlackboardWrites++;
        _metrics.DecisionBlackboardWrites++;
        agent.Bb.Set(key, value);
    }

    private static Faction Opposing(Faction faction) => faction == Faction.Dominion ? Faction.Collective : Faction.Dominion;

    private readonly record struct DecisionWorkResult(int ShipId, ShipActionType Type, long DecisionBlackboardReads);

    private sealed class SensorCadenceState
    {
        public int NextSensorRefreshTick;
        public int LastSensorRefreshTick;
        public int CurrentSensorCadenceTicks;
        public bool ForceSensorRefresh;
        public TacticalSummary? LastTacticalSummary;
        public float LastObservedIntegrity;
    }
}

