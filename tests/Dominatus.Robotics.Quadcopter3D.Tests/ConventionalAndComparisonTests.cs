using Dominatus.Robotics.Quadcopter3D.Conventional;
using Dominatus.Robotics.Quadcopter3D.Shared;

namespace Dominatus.Robotics.Quadcopter3D.Tests;

public sealed class ConventionalAndComparisonTests
{
    [Theory]
    [InlineData(ScenarioKind.Nominal)]
    [InlineData(ScenarioKind.CoupledDisturbance)]
    [InlineData(ScenarioKind.ImuDropout)]
    [InlineData(ScenarioKind.VisionDropout)]
    [InlineData(ScenarioKind.SensorConflict)]
    [InlineData(ScenarioKind.ActuatorDegradation)]
    [InlineData(ScenarioKind.WindImpulse)]
    [InlineData(ScenarioKind.Saturation)]
    [InlineData(ScenarioKind.CameraLatencySpike)]
    public void ConventionalController_MeetsSharedCriteriaWithoutLlmForKnownCases(ScenarioKind kind)
    {
        var result = FaultScenarioRunner.Run(new ConventionalFlightController(), new(kind, new(14, -10, 7)));
        Assert.True(result.RecoverySucceeded, $"{kind}: final={result.FinalAttitude}, rate={result.FinalRate}, modes={string.Join(',', result.Modes)}");
        Assert.Equal(0, result.LlmInvocationCount);
    }

    [Fact]
    public void ConventionalUnknownAnomaly_UsesEquivalentBoundedEscalation()
    {
        var result = FaultScenarioRunner.Run(new ConventionalFlightController(), new(ScenarioKind.UnknownAnomaly, new(8, -6, 4), 400));
        Assert.Equal(1, result.LlmInvocationCount);
        Assert.Contains("SafeHover", result.Modes);
        Assert.Contains("ControlledDescent", result.Modes);
    }

    [Fact]
    public void ConventionalComponentStateAndSchedulingRemainCoherent()
    {
        var controller = new ConventionalFlightController();
        var result = FaultScenarioRunner.Run(controller, new(ScenarioKind.SensorConflict, new(10, -8, 4), 500));
        Assert.Contains("SensorConflict", result.Modes);
        Assert.Equal(6, controller.CallbackHandlerCount);
        Assert.Equal(1, controller.TimerCount);
        Assert.True(controller.MutableStateStoreCount > 1);
        Assert.NotEmpty(controller.Diagnostics.Trace);
    }

    [Fact]
    public void ConventionalAssembly_HasNoDominatusRuntimeDependency()
    {
        var forbidden = new[] { "Dominatus.Core", "Dominatus.OptFlow", "Dominatus.UtilityLite", "Dominatus.Llm.OptFlow" };
        var references = typeof(ConventionalFlightController).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.DoesNotContain(references, name => forbidden.Contains(name, StringComparer.Ordinal));
    }

    [Fact]
    public void ConventionalEmergencyDisarm_DominatesWithoutLlm()
    {
        var result = FaultScenarioRunner.Run(new ConventionalFlightController(), new(ScenarioKind.EmergencyDisarm, new(10, -5, 3), 250));
        Assert.Contains("EmergencyStop", result.Modes);
        Assert.Equal(0, result.LlmInvocationCount);
        Assert.True(result.RecoverySucceeded);
    }

    [Fact]
    public void BothControllersUseTheSameScenarioContractAndSuccessCriteria()
    {
        var scenario = new ScenarioDefinition(ScenarioKind.CommandDelay, new(16, -11, 8));
        var dominatus = FaultScenarioRunner.Run(new DominatusFlightController(), scenario);
        var conventional = FaultScenarioRunner.Run(new ConventionalFlightController(), scenario);
        Assert.True(dominatus.RecoverySucceeded);
        Assert.True(conventional.RecoverySucceeded);
        Assert.Equal(dominatus.CommandCount, conventional.CommandCount);
        Assert.Equal(0, dominatus.LlmInvocationCount + conventional.LlmInvocationCount);
    }

    [Fact]
    public void DelayPredictor_IsComparedAgainstSameDelayedPlant()
    {
        var scenario = new ScenarioDefinition(ScenarioKind.CommandDelay, new(18, -13, 9), 900);
        var predicted = FaultScenarioRunner.Run(new ConventionalFlightController(true), scenario);
        var reactive = FaultScenarioRunner.Run(new ConventionalFlightController(false), scenario);
        Assert.True(predicted.RecoverySucceeded);
        Assert.True(predicted.Overshoot <= reactive.Overshoot + .25f, $"predicted={predicted.Overshoot}, reactive={reactive.Overshoot}");
    }
}
