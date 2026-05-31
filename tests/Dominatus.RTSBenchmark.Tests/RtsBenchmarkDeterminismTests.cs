using Dominatus.RTSBenchmark;

namespace Dominatus.RTSBenchmark.Tests;

public sealed class RtsBenchmarkDeterminismTests
{
    [Fact]
    public void RtsBenchmark_Deterministic_SameRunSameHash()
    {
        var a = RtsBenchmarkRunner.Run(new RtsBenchmarkOptions { WriteCheckpoints = false });
        var b = RtsBenchmarkRunner.Run(new RtsBenchmarkOptions { WriteCheckpoints = false });

        Assert.Equal(a.DeterminismHash, b.DeterminismHash);
        Assert.Equal(a.AgentTicks, b.AgentTicks);
        Assert.Equal(a.DecisionsEvaluated, b.DecisionsEvaluated);
        Assert.Equal(a.ActionsEmitted, b.ActionsEmitted);
        Assert.Equal(a.EventsDelivered, b.EventsDelivered);
        Assert.Equal(a.DamageEvents, b.DamageEvents);
        Assert.Equal(a.RepairEvents, b.RepairEvents);
        Assert.Equal(a.DestroyedShips, b.DestroyedShips);
    }
}
