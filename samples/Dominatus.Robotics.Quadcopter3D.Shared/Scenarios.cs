namespace Dominatus.Robotics.Quadcopter3D.Shared;

public enum ScenarioKind
{
    Nominal, CoupledDisturbance, ImuDropout, VisionDropout, SensorConflict, ActuatorDegradation,
    WindImpulse, CommandDelay, Saturation, EmergencyDisarm, UnknownAnomaly, CameraLatencySpike
}

public sealed record ScenarioDefinition(ScenarioKind Kind, Axis3 InitialAttitude, int Ticks = 900, bool PredictorEnabled = true);

public sealed record ScenarioMetrics(
    string Controller,
    ScenarioKind Scenario,
    float PeakRollError,
    float PeakPitchError,
    float PeakYawError,
    float SettlingTimeSeconds,
    float Overshoot,
    float SaturationDurationSeconds,
    float DegradedModeSeconds,
    int CommandCount,
    int VisionOperationCount,
    int LlmInvocationCount,
    bool RecoverySucceeded,
    IReadOnlyList<string> Modes,
    Axis3 FinalAttitude,
    Axis3 FinalRate,
    string? Diagnostic);

public static class FaultScenarioRunner
{
    public static ScenarioMetrics Run(IQuadcopterController controller, ScenarioDefinition scenario)
    {
        const float dt = .02f;
        var plant = new QuadcopterPlant(scenario.InitialAttitude, dt, scenario.Kind == ScenarioKind.CommandDelay ? .10f : .06f);
        var sensors = new SensorSuite();
        var peak = new Axis3(MathF.Abs(plant.AttitudeDegrees.Roll), MathF.Abs(plant.AttitudeDegrees.Pitch), MathF.Abs(plant.AttitudeDegrees.Yaw));
        var initialMagnitude = plant.AttitudeDegrees.MaxAbs;
        var overshoot = 0f;
        var settledAt = float.NaN;
        var degradedTicks = 0;
        var modes = new HashSet<string>(StringComparer.Ordinal);

        for (var tick = 0; tick < scenario.Ticks; tick++)
        {
            var faults = FaultsFor(scenario.Kind, tick);
            plant.FrontLeftAuthority = scenario.Kind == ScenarioKind.ActuatorDegradation && tick >= 150 ? .55f : 1f;
            plant.ExternalTorque = TorqueFor(scenario.Kind, tick);
            var emergency = scenario.Kind == ScenarioKind.EmergencyDisarm && tick >= 120;
            var observation = sensors.Observe(plant, tick * dt, faults, emergency);
            var command = controller.Update(observation, dt);
            if (emergency) plant.SetArmed(false);
            plant.Step(command);

            peak = new(MathF.Max(peak.Roll, MathF.Abs(plant.AttitudeDegrees.Roll)), MathF.Max(peak.Pitch, MathF.Abs(plant.AttitudeDegrees.Pitch)), MathF.Max(peak.Yaw, MathF.Abs(plant.AttitudeDegrees.Yaw)));
            overshoot = MathF.Max(overshoot, MathF.Max(0, plant.AttitudeDegrees.MaxAbs - initialMagnitude));
            modes.Add(controller.Diagnostics.Mode);
            foreach (var entry in controller.Diagnostics.Trace)
                if (entry.StartsWith("state:", StringComparison.Ordinal)) modes.Add(entry[6..]);
            if (controller.Diagnostics.Mode.Contains("Degraded", StringComparison.Ordinal) || controller.Diagnostics.Mode.Contains("Conflict", StringComparison.Ordinal) || controller.Diagnostics.Mode.Contains("Recovery", StringComparison.Ordinal) || controller.Diagnostics.Mode.Contains("Safe", StringComparison.Ordinal)) degradedTicks++;
            if (float.IsNaN(settledAt) && plant.AttitudeDegrees.MaxAbs < 2f && plant.RateDegreesPerSecond.MaxAbs < 4f) settledAt = tick * dt;
        }

        var shouldBeDisarmed = scenario.Kind == ScenarioKind.EmergencyDisarm;
        var recovered = shouldBeDisarmed ? !plant.Armed : plant.AttitudeDegrees.MaxAbs < (scenario.Kind is ScenarioKind.SensorConflict or ScenarioKind.UnknownAnomaly ? 18f : 6f);
        return new(controller.Name, scenario.Kind, peak.Roll, peak.Pitch, peak.Yaw,
            float.IsNaN(settledAt) ? scenario.Ticks * dt : settledAt, overshoot, plant.SaturationSeconds,
            degradedTicks * dt, plant.CommandCount, controller.Diagnostics.VisionOperationCount,
            controller.Diagnostics.LlmInvocationCount, recovered, modes.Order().ToArray(), plant.AttitudeDegrees, plant.RateDegreesPerSecond, controller.Diagnostics.PendingOperation);
    }

    private static SensorFaults FaultsFor(ScenarioKind kind, int tick) => kind switch
    {
        ScenarioKind.ImuDropout when tick is >= 150 and < 330 => new(ImuDropout: true),
        ScenarioKind.VisionDropout when tick is >= 150 and < 330 => new(VisionDropout: true),
        ScenarioKind.SensorConflict when tick is >= 150 and < 450 => new(VisionBias: new Axis3(28, -22, 0)),
        ScenarioKind.UnknownAnomaly when tick >= 150 => new(UnknownAnomaly: true),
        ScenarioKind.CameraLatencySpike when tick is >= 150 and < 300 => new(VisionLatencyTicks: tick % 20 < 10 ? 8 : 0),
        _ => new()
    };

    private static Axis3 TorqueFor(ScenarioKind kind, int tick) => kind switch
    {
        ScenarioKind.CoupledDisturbance when tick is >= 120 and < 140 => new(38, -32, 18),
        ScenarioKind.WindImpulse when tick is >= 180 and < 200 => new(-42, 36, 16),
        ScenarioKind.Saturation when tick is >= 100 and < 350 => new(85, -70, 28),
        _ => Axis3.Zero
    };
}
