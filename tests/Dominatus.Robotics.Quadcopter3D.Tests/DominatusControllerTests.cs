using Dominatus.Core.Persistence;
using Dominatus.Robotics.Quadcopter3D.Shared;

namespace Dominatus.Robotics.Quadcopter3D.Tests;

public sealed class DominatusControllerTests
{
    [Fact]
    public void GeneratedFlow_ContainsOnlyAuthoredDurableStates()
    {
        var inspection = DominatusFlightFlow.Define().Inspect();
        Assert.Equal(19, inspection.States.Count);
        Assert.Empty(inspection.GeneratedArtifacts);
        Assert.Contains(inspection.States, s => s.Id.Value == "SensorConflict");
        Assert.Contains(inspection.States, s => s.Id.Value == "EscalateNovelCondition");
    }

    [Theory]
    [InlineData(18, -12, 9)]
    [InlineData(-18, 12, -9)]
    public void NominalController_StabilizesAllAxes(float roll, float pitch, float yaw)
    {
        var result = FaultScenarioRunner.Run(new DominatusFlightController(), new(ScenarioKind.Nominal, new(roll, pitch, yaw)));
        Assert.True(result.RecoverySucceeded, $"final={result.FinalAttitude}, rate={result.FinalRate}, settling={result.SettlingTimeSeconds}, modes={string.Join(',', result.Modes)}, diag={result.Diagnostic}");
        Assert.Equal(0, result.LlmInvocationCount);
    }

    [Theory]
    [InlineData(ScenarioKind.CoupledDisturbance)]
    [InlineData(ScenarioKind.ImuDropout)]
    [InlineData(ScenarioKind.VisionDropout)]
    [InlineData(ScenarioKind.SensorConflict)]
    [InlineData(ScenarioKind.ActuatorDegradation)]
    [InlineData(ScenarioKind.WindImpulse)]
    [InlineData(ScenarioKind.Saturation)]
    [InlineData(ScenarioKind.CameraLatencySpike)]
    public void KnownFaults_UseAuthoredRecoveryWithoutLlm(ScenarioKind kind)
    {
        var result = FaultScenarioRunner.Run(new DominatusFlightController(), new(kind, new(14, -10, 7)));
        Assert.Equal(0, result.LlmInvocationCount);
        Assert.True(result.RecoverySucceeded, $"{kind}: final={result.FinalAttitude}, rate={result.FinalRate}, modes={string.Join(',', result.Modes)}");
        Assert.True(result.VisionOperationCount > 0);
    }

    [Fact]
    public void UnknownAnomaly_EntersSafeStateThenInvokesBoundedLlm()
    {
        var result = FaultScenarioRunner.Run(new DominatusFlightController(), new(ScenarioKind.UnknownAnomaly, new(8, -6, 4), 400));
        Assert.Equal(1, result.LlmInvocationCount);
        Assert.Contains("SafeHover", result.Modes);
        Assert.Contains("EscalateNovelCondition", result.Modes);
        Assert.Contains("ControlledDescent", result.Modes);
    }

    [Fact]
    public void EmergencyDisarm_Dominates()
    {
        var result = FaultScenarioRunner.Run(new DominatusFlightController(), new(ScenarioKind.EmergencyDisarm, new(10, -5, 3), 250));
        Assert.Contains("EmergencyStop", result.Modes);
        Assert.Equal(0, result.LlmInvocationCount);
        Assert.True(result.RecoverySucceeded);
    }

    [Fact]
    public void Checkpoint_RestoresControllerStateAndOperationIdentity()
    {
        var controller = new DominatusFlightController();
        var sensors = new SensorSuite();
        var plant = new QuadcopterPlant(new Axis3(12, -8, 5));
        for (var i = 0; i < 8; i++) controller.Update(sensors.Observe(plant, i * .02, new()), .02f);
        var checkpoint = DominatusCheckpointBuilder.Capture(controller.World);
        var expectedMode = controller.Agent.Bb.GetOrDefault(DominatusFlightFlow.Memory.Mode, string.Empty);
        controller.Agent.Bb.Set(DominatusFlightFlow.Memory.Mode, "corrupt");
        DominatusCheckpointBuilder.Restore(controller.World, checkpoint);
        Assert.Equal(expectedMode, controller.Agent.Bb.GetOrDefault(DominatusFlightFlow.Memory.Mode, string.Empty));
        Assert.Equal("quad3.vision.estimate", DominatusFlightFlow.EstimateVision.Id.Value);
    }

    [Fact]
    public void PendingVisionOperation_CheckpointsRestoresAndReplaysWithoutRedispatch()
    {
        var original = new DominatusFlightController();
        var sensors = new SensorSuite();
        var plant = new QuadcopterPlant(new Axis3(12, -8, 5));
        for (var i = 0; i < 20 && original.Agent.InFlightActuations.Count == 0; i++)
            original.Update(sensors.Observe(plant, i * .02, new()), .02f);
        Assert.Single(original.Agent.InFlightActuations);
        var checkpoint = DominatusCheckpointBuilder.Capture(original.World);

        var restored = new DominatusFlightController();
        var cursors = DominatusCheckpointBuilder.Restore(restored.World, checkpoint);
        var replay = new ReplayDriver(restored.World,
            new ReplayLog(1, [new ReplayEvent.Text(restored.Agent.Id.ToString(), "opencv-hough|0.9000|12.0000|-8.0000|5")]), cursors);
        replay.ApplyAll();
        for (var i = 0; i < 6; i++) restored.World.Tick(.01f);

        Assert.Equal("opencv-hough|0.9000|12.0000|-8.0000|5", restored.Agent.Bb.GetOrDefault(DominatusFlightFlow.Memory.VisionResult, string.Empty));
        Assert.Equal(0, restored.VisionDispatchCount);
        Assert.DoesNotContain(DominatusFlightFlow.States.VisionOperation, restored.Agent.Brain.GetActivePath());
    }

    [Fact]
    public void ParallelControllerInstances_DoNotShareState()
    {
        var left = new DominatusFlightController();
        var right = new DominatusFlightController();
        left.Agent.Bb.Set(DominatusFlightFlow.Memory.Roll, 77);
        Assert.NotEqual(77, right.Agent.Bb.GetOrDefault(DominatusFlightFlow.Memory.Roll, 0));
    }
}
