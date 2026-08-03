using Dominatus.Robotics.Quadcopter3D.Shared;

namespace Dominatus.Robotics.Quadcopter3D.Tests;

public sealed class SharedSimulationTests
{
    [Fact]
    public void Plant_ModelsDelayCouplingAndAuthorityLoss()
    {
        var plant = new QuadcopterPlant(new Axis3(0, 0, 0), .02f, .06f) { FrontLeftAuthority = .5f };
        var command = MotorMixer.Mix(.52f, new Axis3(.3f, .1f, .08f));
        plant.Step(command);
        Assert.Equal(Axis3.Zero, plant.RateDegreesPerSecond);
        for (var i = 0; i < 8; i++) plant.Step(command);
        Assert.True(plant.RateDegreesPerSecond.MaxAbs > 0);
        Assert.NotEqual(0, plant.RateDegreesPerSecond.Yaw);
    }

    [Fact]
    public void OpenCvPipeline_RecoversSyntheticHorizon()
    {
        var pipeline = new VisionPipeline();
        var result = pipeline.Estimate(pipeline.Render(7, .14, 14, -6));
        Assert.True(result.Healthy, result.Status);
        Assert.InRange(result.RollDegrees, 11, 17);
        Assert.InRange(result.PitchDegrees, -8, -4);
        Assert.Equal("opencv-hough", result.Status);
    }

    [Fact]
    public void MiniSmithPredictor_ProjectsKnownPendingCommand()
    {
        var predictor = new MiniSmithPredictor(.10f, new Axis3(250, 235, 145), 1.3f);
        var noCommand = predictor.Predict(new Axis3(10, 0, 0), new Axis3(5, 0, 0), []);
        var withCommand = predictor.Predict(new Axis3(10, 0, 0), new Axis3(5, 0, 0), [MotorMixer.Mix(.5f, new Axis3(-.2f, 0, 0))]);
        Assert.True(withCommand.Roll < noCommand.Roll);
        Assert.Equal(withCommand, predictor.Predict(new Axis3(10, 0, 0), new Axis3(5, 0, 0), [MotorMixer.Mix(.5f, new Axis3(-.2f, 0, 0))]));
    }
}
