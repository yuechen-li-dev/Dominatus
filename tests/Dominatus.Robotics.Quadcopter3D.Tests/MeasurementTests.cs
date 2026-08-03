using System.Diagnostics;
using Dominatus.Robotics.Quadcopter3D.Conventional;
using Dominatus.Robotics.Quadcopter3D.Shared;
using Xunit.Abstractions;

namespace Dominatus.Robotics.Quadcopter3D.Tests;

public sealed class MeasurementTests(ITestOutputHelper output)
{
    [Fact]
    public void SharedScenarioMatrix_ProducesBehaviorAndTimingMeasurements()
    {
        var scenarios = new[]
        {
            ScenarioKind.Nominal, ScenarioKind.CoupledDisturbance, ScenarioKind.ImuDropout,
            ScenarioKind.VisionDropout, ScenarioKind.SensorConflict, ScenarioKind.ActuatorDegradation,
            ScenarioKind.WindImpulse, ScenarioKind.CommandDelay, ScenarioKind.Saturation,
            ScenarioKind.EmergencyDisarm, ScenarioKind.UnknownAnomaly, ScenarioKind.CameraLatencySpike
        };

        foreach (var kind in scenarios)
        {
            foreach (var controller in new IQuadcopterController[] { new DominatusFlightController(), new ConventionalFlightController() })
            {
                var beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
                var timer = Stopwatch.StartNew();
                var metrics = FaultScenarioRunner.Run(controller, new(kind, new Axis3(14, -10, 7)));
                timer.Stop();
                var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
                output.WriteLine($"{kind}|{controller.Name}|peak={metrics.PeakRollError:F2}/{metrics.PeakPitchError:F2}/{metrics.PeakYawError:F2}|settle={metrics.SettlingTimeSeconds:F2}|overshoot={metrics.Overshoot:F2}|sat={metrics.SaturationDurationSeconds:F2}|degraded={metrics.DegradedModeSeconds:F2}|vision={metrics.VisionOperationCount}|llm={metrics.LlmInvocationCount}|ok={metrics.RecoverySucceeded}|elapsedMs={timer.Elapsed.TotalMilliseconds:F2}|alloc={allocated}");
                Assert.True(metrics.RecoverySucceeded);
            }
        }
    }
}
