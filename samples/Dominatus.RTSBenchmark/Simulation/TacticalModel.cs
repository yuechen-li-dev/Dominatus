namespace Dominatus.RTSBenchmark.Simulation;

public static class TacticalModel
{
    public static TacticalSummary ComputeSummary(
        ShipState ship,
        IReadOnlyList<ShipState> ships,
        int commandFocusTargetId,
        ref TacticalContactCounters counters)
    {
        var ownDef = ShipClassDefinition.Get(ship.Class);
        var ownHullFraction = HullFraction(ship, ownDef);
        var doctrine = DoctrineProfile.For(ship.Faction);
        var bestImmediateThreatScore = 0f;
        int? immediateThreatId = null;
        var bestAttackScore = 0f;
        int? bestAttackTargetId = null;
        var bestAttackBand = TacticalDistanceBand.OutOfRange;
        var bestRepairScore = 0f;
        int? bestRepairTargetId = null;
        var bestHighValueScore = 0f;
        int? highestValueVisibleEnemyId = null;
        var localThreatScore = 0f;
        var localSupportScore = 0f;
        var relevantEnemies = 0;
        var relevantAllies = 0;

        foreach (var other in ships)
        {
            counters.SensorPairsChecked++;
            if (!other.Alive || other.Id == ship.Id) continue;

            var distance = Distance(ship, other);
            var otherDef = ShipClassDefinition.Get(other.Class);
            var band = ClassifyBand(distance, ownDef, otherDef, other.Faction != ship.Faction);
            if (band == TacticalDistanceBand.OutOfRange)
            {
                counters.IgnoredOutOfRangeContacts++;
                continue;
            }

            CountBand(band, ref counters);
            if (other.Faction != ship.Faction)
            {
                relevantEnemies++;
                counters.RelevantEnemyContacts++;
                var threat = ComputeThreatScore(ship, ownDef, ownHullFraction, other, otherDef, distance, band);
                localThreatScore += threat;
                if (band == TacticalDistanceBand.Immediate &&
                    (threat > bestImmediateThreatScore || TieBreak(threat, bestImmediateThreatScore, other.Id, immediateThreatId)))
                {
                    bestImmediateThreatScore = threat;
                    immediateThreatId = other.Id;
                }

                var priority = ComputeAttackPriority(ship, ownDef, other, otherDef, distance, band, doctrine, commandFocusTargetId);
                if (priority > bestAttackScore || TieBreak(priority, bestAttackScore, other.Id, bestAttackTargetId))
                {
                    bestAttackScore = priority;
                    bestAttackTargetId = other.Id;
                    bestAttackBand = band;
                }

                var highValue = otherDef.RoleWeight * RolePriority(other.Class, doctrine) * BandWeight(band);
                if (highValue > bestHighValueScore || TieBreak(highValue, bestHighValueScore, other.Id, highestValueVisibleEnemyId))
                {
                    bestHighValueScore = highValue;
                    highestValueVisibleEnemyId = other.Id;
                }
            }
            else
            {
                relevantAllies++;
                counters.RelevantAllyContacts++;
                var support = ComputeSupportScore(ship, ownDef, other, otherDef, distance, band, doctrine);
                localSupportScore += support;
                if (support > bestRepairScore || TieBreak(support, bestRepairScore, other.Id, bestRepairTargetId))
                {
                    bestRepairScore = support;
                    bestRepairTargetId = other.Id;
                }
            }
        }

        return new TacticalSummary
        {
            ImmediateThreatId = immediateThreatId,
            BestAttackTargetId = bestAttackTargetId,
            BestRepairTargetId = bestRepairTargetId,
            HighestValueVisibleEnemyId = highestValueVisibleEnemyId,
            LocalThreatScore = Math.Clamp(localThreatScore, 0f, 1.5f),
            LocalSupportScore = Math.Clamp(localSupportScore, 0f, 1.5f),
            RelevantEnemyContacts = relevantEnemies,
            RelevantAllyContacts = relevantAllies,
            BestAttackTargetBand = bestAttackBand,
            BestAttackPriorityScore = Math.Clamp(bestAttackScore, 0f, 2f)
        };
    }

    public static TacticalDistanceBand ClassifyBand(float distance, ShipClassDefinition ownDef, ShipClassDefinition otherDef, bool isEnemy)
    {
        if (distance <= ownDef.Range || (isEnemy && distance <= otherDef.Range)) return TacticalDistanceBand.Immediate;
        if (distance <= ownDef.SensorRange * 0.5f) return TacticalDistanceBand.Near;
        if (distance <= ownDef.SensorRange) return TacticalDistanceBand.Sensor;
        return TacticalDistanceBand.OutOfRange;
    }

    private static float ComputeThreatScore(
        ShipState ship,
        ShipClassDefinition ownDef,
        float ownHullFraction,
        ShipState enemy,
        ShipClassDefinition enemyDef,
        float distance,
        TacticalDistanceBand band)
    {
        var classThreat = Math.Clamp(enemyDef.Damage / 48f * 0.58f + enemyDef.RoleWeight / 2.8f * 0.42f, 0f, 1.35f);
        var targetVulnerability = 1f - ownHullFraction;
        var rangeRelation = distance <= enemyDef.Range ? 1.12f : distance <= ownDef.Range ? 1.02f : 0.92f;
        var readiness = enemy.CooldownRemaining <= 0 ? 1.08f : 0.88f;
        return Math.Clamp(BandWeight(band) * classThreat * rangeRelation * readiness * (0.5f + 0.5f * targetVulnerability), 0f, 1f);
    }

    private static float ComputeAttackPriority(
        ShipState ship,
        ShipClassDefinition ownDef,
        ShipState enemy,
        ShipClassDefinition enemyDef,
        float distance,
        TacticalDistanceBand band,
        DoctrineProfile doctrine,
        int commandFocusTargetId)
    {
        var enemyHullFraction = HullFraction(enemy, enemyDef);
        var closeness = 1f - Math.Clamp(distance / Math.Max(1f, ownDef.SensorRange), 0f, 1f);
        var damaged = 1f - enemyHullFraction;
        var immediateBonus = band == TacticalDistanceBand.Immediate ? 0.34f : band == TacticalDistanceBand.Near ? 0.14f : 0f;
        var commandFocus = commandFocusTargetId == enemy.Id ? 0.50f : 0f;
        var role = RolePriority(enemy.Class, doctrine);
        var baseScore = enemyDef.RoleWeight / 2.8f * 0.44f + closeness * 0.25f + damaged * 0.18f + immediateBonus + commandFocus;
        return Math.Clamp(BandWeight(band) * baseScore * role * doctrine.Aggression, 0f, 2f);
    }

    private static float ComputeSupportScore(
        ShipState ship,
        ShipClassDefinition ownDef,
        ShipState ally,
        ShipClassDefinition allyDef,
        float distance,
        TacticalDistanceBand band,
        DoctrineProfile doctrine)
    {
        if (ownDef.RepairAmount <= 0f) return 0f;
        var damageNeed = 1f - HullFraction(ally, allyDef);
        if (damageNeed <= 0.04f) return 0f;
        var closeness = 1f - Math.Clamp(distance / Math.Max(1f, Math.Max(ownDef.Range, 30f)), 0f, 1f);
        var value = allyDef.RoleWeight / 2.8f * doctrine.PreserveHighValueShips;
        return Math.Clamp(BandWeight(band) * (damageNeed * 0.62f + closeness * 0.23f + value * 0.15f) * doctrine.RepairPriority, 0f, 1.5f);
    }

    private static float RolePriority(ShipClass shipClass, DoctrineProfile doctrine) => shipClass switch
    {
        ShipClass.CommandCruiser or ShipClass.SynapseCruiser => doctrine.FocusCommandTargets * 1.28f,
        ShipClass.Carrier or ShipClass.HiveArk => 1.20f,
        ShipClass.RepairTender or ShipClass.Regenerator => 1.15f,
        ShipClass.RailgunDestroyer => 1.08f,
        ShipClass.NeedleDrone => 0.86f,
        _ => 1f
    };

    public static float BandWeight(TacticalDistanceBand band) => band switch
    {
        TacticalDistanceBand.Immediate => 1.00f,
        TacticalDistanceBand.Near => 0.55f,
        TacticalDistanceBand.Sensor => 0.20f,
        _ => 0f
    };

    private static void CountBand(TacticalDistanceBand band, ref TacticalContactCounters counters)
    {
        switch (band)
        {
            case TacticalDistanceBand.Immediate:
                counters.ImmediateThreatContacts++;
                break;
            case TacticalDistanceBand.Near:
                counters.NearContacts++;
                break;
            case TacticalDistanceBand.Sensor:
                counters.SensorBandContacts++;
                break;
        }
    }

    private static bool TieBreak(float value, float best, int id, int? bestId) => Math.Abs(value - best) < 0.0001f && id < (bestId ?? int.MaxValue);

    private static float HullFraction(ShipState ship, ShipClassDefinition def) => Math.Clamp(ship.Hull / Math.Max(1f, def.Hull), 0f, 1f);

    private static float Distance(ShipState a, ShipState b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
