using Dominatus.RTSBenchmark.Simulation;

namespace Dominatus.RTSBenchmark;

public static class BattleReport
{
    public static void Write(TextWriter output, RtsBenchmarkResult result)
    {
        output.WriteLine("Dominatus.RTSBenchmark");
        output.WriteLine($"Mode: {result.Mode}");
        output.WriteLine($"Ticks simulated: {result.TicksSimulated}");
        output.WriteLine($"Ships: initial {result.InitialShips}, final {result.FinalShips}, destroyed {result.DestroyedShips}");
        output.WriteLine($"Winner: {result.Winner?.ToString() ?? "Draw"}");
        output.WriteLine($"Fleet power: Dominion {result.DominionFleetPower:0.00} | Collective {result.CollectiveFleetPower:0.00}");
        output.WriteLine($"Agent ticks: {result.AgentTicks}");
        output.WriteLine($"Decisions evaluated: {result.DecisionsEvaluated}");
        output.WriteLine($"Actions emitted: {result.ActionsEmitted} (Dominion {result.DominionActions}, Collective {result.CollectiveActions})");
        output.WriteLine($"Events delivered: {result.EventsDelivered} (Dominion {result.DominionEvents}, Collective {result.CollectiveEvents})");
        output.WriteLine($"Damage events: {result.DamageEvents}");
        output.WriteLine($"Repair events: {result.RepairEvents}");
        output.WriteLine($"Elapsed wall clock: {result.ElapsedWallClock.TotalMilliseconds:0.00} ms");
        output.WriteLine($"Primary score AgentTicksPerSecond: {result.AgentTicksPerSecond:0.00}");
        output.WriteLine($"DecisionsPerSecond: {result.DecisionsPerSecond:0.00}");
        output.WriteLine($"ActionsPerSecond: {result.ActionsPerSecond:0.00}");
        output.WriteLine($"EventsPerSecond: {result.EventsPerSecond:0.00}");
        output.WriteLine($"Determinism hash: {result.DeterminismHash}");
        output.WriteLine("No rendering, GPU, LLM, network, or provider calls are used.");
    }
}
