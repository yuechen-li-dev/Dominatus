using Dominatus.Robotics.Quadcopter;

namespace Dominatus.Robotics.Quadcopter.Tests;

public sealed class QuadcopterAttitudeControllerTests
{
    [Theory]
    [InlineData(18f)]
    [InlineData(-18f)]
    public void AuthoredHybridController_StabilizesRollPlant(float initialRoll)
    {
        var world = QuadcopterAttitudeController.CreateSimulation(initialRoll, out var vehicle, out var plant);

        for (var i = 0; i < 1200; i++) world.Tick(QuadcopterAttitudeController.ControlPeriodSeconds);

        var roll = vehicle.Bb.GetOrDefault(QuadcopterAttitudeController.Memory.RollDegrees, float.NaN);
        var rate = vehicle.Bb.GetOrDefault(QuadcopterAttitudeController.Memory.RollRateDegreesPerSecond, float.NaN);
        Assert.True(MathF.Abs(roll) < QuadcopterAttitudeController.LevelToleranceDegrees, $"roll={roll}");
        Assert.True(MathF.Abs(rate) < QuadcopterAttitudeController.RateToleranceDegreesPerSecond, $"rate={rate}");
        Assert.NotEmpty(plant.Commands);
    }

    [Fact]
    public void PositiveRoll_SelectsNegativeCorrectiveMotorTorque()
    {
        var world = QuadcopterAttitudeController.CreateSimulation(15f, out var vehicle, out var plant);
        TickUntilCommand(world, plant);
        Assert.Equal(QuadcopterAttitudeController.States.CorrectPositiveRoll.Value,
            vehicle.Bb.GetOrDefault(QuadcopterAttitudeController.Memory.LastControlMode, ""));
        Assert.True(plant.Commands[0].RollTorque < 0f);
    }

    [Fact]
    public void Disarm_DominatesAndCommandsZeroCollective()
    {
        var world = QuadcopterAttitudeController.CreateSimulation(15f, out var vehicle, out var plant);
        vehicle.Bb.Set(QuadcopterAttitudeController.Memory.DisarmRequested, true);
        TickUntilCommand(world, plant);
        Assert.Equal(0f, plant.Commands[0].Collective);
        Assert.Equal(0f, plant.Commands[0].RollTorque);
    }

    [Fact]
    public void ControllerHasExplicitStatesAndNoGeneratedStates()
    {
        var inspection = QuadcopterAttitudeController.Define().Inspect();
        Assert.Equal(7, inspection.States.Count);
        Assert.Empty(inspection.GeneratedArtifacts);
        Assert.Equal("quad.control.apply-motor-mix", QuadcopterAttitudeController.ApplyMotorMix.Id.Value);
    }

    [Fact]
    public void GeneratedDefinition_ContainsOnlyTheSevenAuthoredDurableIds()
    {
        var ids = QuadcopterAttitudeController.Define().Inspect().States.Select(state => state.Id.Value).ToArray();
        Assert.Equal(["ControlLoop", "BrakeNegativeRate", "BrakePositiveRate", "CorrectNegativeRoll", "CorrectPositiveRoll", "Disarmed", "HoldLevel"], ids);
    }

    private static void TickUntilCommand(Dominatus.Core.Runtime.AiWorld world, QuadcopterRollPlant plant)
    {
        for (var i = 0; i < 20 && plant.Commands.Count == 0; i++) world.Tick(QuadcopterAttitudeController.ControlPeriodSeconds);
        Assert.NotEmpty(plant.Commands);
    }
}
