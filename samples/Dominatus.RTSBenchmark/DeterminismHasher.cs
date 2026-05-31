using System.Security.Cryptography;
using System.Text;
using Dominatus.RTSBenchmark.Simulation;

namespace Dominatus.RTSBenchmark;

public static class DeterminismHasher
{
    public static string Compute(
        BenchmarkMode mode,
        int ticks,
        int initialShips,
        IReadOnlyList<ShipState> ships,
        BenchmarkMetrics metrics,
        Faction? winner,
        float dominionFleetPower,
        float collectiveFleetPower)
    {
        var sb = new StringBuilder(capacity: ships.Count * 64);
        sb.Append("mode=").Append(mode).Append(";ticks=").Append(ticks).Append(";initial=").Append(initialShips).Append(';');
        sb.Append("final=").Append(ships.Count(s => s.Alive)).Append(";winner=").Append(winner?.ToString() ?? "Draw").Append(';');
        sb.Append("agentTicks=").Append(metrics.AgentTicks).Append(";decisions=").Append(metrics.DecisionsEvaluated)
            .Append(";actions=").Append(metrics.ActionsEmitted).Append(";events=").Append(metrics.EventsDelivered)
            .Append(";damage=").Append(metrics.DamageEvents).Append(";repair=").Append(metrics.RepairEvents)
            .Append(";destroyed=").Append(metrics.DestroyedShips).Append(';');
        sb.Append("powerD=").Append(Q(dominionFleetPower)).Append(";powerC=").Append(Q(collectiveFleetPower)).Append(';');

        foreach (var ship in ships.OrderBy(s => s.Id))
        {
            sb.Append("ship=").Append(ship.Id).Append(',').Append(ship.Faction).Append(',').Append(ship.Class).Append(',')
                .Append(ship.Alive ? '1' : '0').Append(',')
                .Append(Q(ship.Hull)).Append(',')
                .Append(Q(ship.ShieldOrCarapace)).Append(',')
                .Append(Q(ship.X)).Append(',')
                .Append(Q(ship.Y)).Append(',')
                .Append(ship.CooldownRemaining).Append(';');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }

    private static int Q(float value) => (int)MathF.Round(value * 100f);
}
