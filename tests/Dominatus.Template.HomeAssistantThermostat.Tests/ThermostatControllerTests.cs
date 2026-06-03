using Dominatus.Template.HomeAssistantThermostat;

public sealed class ThermostatControllerTests
{
    [Fact]
    public void BelowTargetChoosesHeat()
    {
        var controller = new ThermostatController(new ThermostatPolicy(0.5, 0));

        var decision = controller.Decide(new ThermostatReading(67, 70, 0.5, ThermostatMode.Idle));

        Assert.Equal(ThermostatMode.Heat, decision.CommittedMode);
        Assert.True(decision.CommandRequired);
    }

    [Fact]
    public void AboveTargetChoosesCool()
    {
        var controller = new ThermostatController(new ThermostatPolicy(0.5, 0));

        var decision = controller.Decide(new ThermostatReading(74, 70, 0.5, ThermostatMode.Idle));

        Assert.Equal(ThermostatMode.Cool, decision.CommittedMode);
        Assert.True(decision.CommandRequired);
    }

    [Fact]
    public void WithinDeadbandChoosesIdle()
    {
        var controller = new ThermostatController(new ThermostatPolicy(0.5, 0));

        var decision = controller.Decide(new ThermostatReading(70.2, 70, 0.5, ThermostatMode.Idle));

        Assert.Equal(ThermostatMode.Idle, decision.CommittedMode);
        Assert.False(decision.CommandRequired);
    }

    [Fact]
    public void HysteresisPreventsImmediateFlipFromHeatToIdleNearTarget()
    {
        var controller = new ThermostatController(new ThermostatPolicy(0.5, 0));
        _ = controller.Decide(new ThermostatReading(67, 70, 0.5, ThermostatMode.Idle));

        var decision = controller.Decide(new ThermostatReading(70.1, 70, 0.5, ThermostatMode.Heat));

        Assert.Equal(ThermostatMode.Heat, decision.CommittedMode);
        Assert.False(decision.CommandRequired);
        Assert.Contains("hysteresis", decision.Reason);
    }

    [Fact]
    public async Task MinCommitPreventsThrashingAcrossMultipleTicks()
    {
        var actuator = new FakeHomeAssistantThermostatActuator();
        var controller = new ThermostatController(new ThermostatPolicy(0, 3));
        var workflow = new ThermostatWorkflow(controller, actuator, "climate.test");

        var result = await workflow.RunAsync([
            new ThermostatTickInput(67, 70),
            new ThermostatTickInput(74, 70),
            new ThermostatTickInput(67, 70)
        ], deadband: 0.5);

        Assert.Single(result.Commands);
        Assert.All(result.Decisions, decision => Assert.Equal(ThermostatMode.Heat, decision.CommittedMode));
        Assert.Contains(result.Decisions, decision => decision.Reason.Contains("min_commit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FakeActuatorRecordsExpectedCommand()
    {
        var actuator = new FakeHomeAssistantThermostatActuator();
        var workflow = new ThermostatWorkflow(new ThermostatController(new ThermostatPolicy(0.5, 0)), actuator, "climate.living_room");

        var result = await workflow.RunAsync([new ThermostatTickInput(67, 70)], 0.5);

        var command = Assert.Single(result.Commands);
        Assert.Equal("climate.living_room", command.EntityId);
        Assert.Equal("heat", command.HvacMode);
        Assert.Single(actuator.Commands);
    }

    [Fact]
    public async Task LiveModeRefusesWithoutRequiredEnvVars()
    {
        var previousUrl = Environment.GetEnvironmentVariable("HOMEASSISTANT_URL");
        var previousToken = Environment.GetEnvironmentVariable("HOMEASSISTANT_TOKEN");
        var previousEntity = Environment.GetEnvironmentVariable("HOMEASSISTANT_CLIMATE_ENTITY");
        Environment.SetEnvironmentVariable("HOMEASSISTANT_URL", null);
        Environment.SetEnvironmentVariable("HOMEASSISTANT_TOKEN", null);
        Environment.SetEnvironmentVariable("HOMEASSISTANT_CLIMATE_ENTITY", null);
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = await ThermostatCli.RunAsync(["--live", "--target-temp", "70"], output, error);

            Assert.Equal(2, code);
            Assert.Contains("HOMEASSISTANT_CLIMATE_ENTITY", error.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOMEASSISTANT_URL", previousUrl);
            Environment.SetEnvironmentVariable("HOMEASSISTANT_TOKEN", previousToken);
            Environment.SetEnvironmentVariable("HOMEASSISTANT_CLIMATE_ENTITY", previousEntity);
        }
    }

    [Fact]
    public async Task DryRunPrintsCommandButDoesNotCallLiveActuator()
    {
        var previousUrl = Environment.GetEnvironmentVariable("HOMEASSISTANT_URL");
        var previousToken = Environment.GetEnvironmentVariable("HOMEASSISTANT_TOKEN");
        var previousEntity = Environment.GetEnvironmentVariable("HOMEASSISTANT_CLIMATE_ENTITY");
        Environment.SetEnvironmentVariable("HOMEASSISTANT_URL", "http://homeassistant.local:8123");
        Environment.SetEnvironmentVariable("HOMEASSISTANT_TOKEN", "test-token");
        Environment.SetEnvironmentVariable("HOMEASSISTANT_CLIMATE_ENTITY", "climate.test");
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = await ThermostatCli.RunAsync(["--live", "--dry-run", "--current-temp", "67", "--target-temp", "70"], output, error);

            Assert.Equal(0, code);
            Assert.Contains("dry-run would call climate.set_hvac_mode heat", output.ToString());
            Assert.Empty(error.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOMEASSISTANT_URL", previousUrl);
            Environment.SetEnvironmentVariable("HOMEASSISTANT_TOKEN", previousToken);
            Environment.SetEnvironmentVariable("HOMEASSISTANT_CLIMATE_ENTITY", previousEntity);
        }
    }

    [Fact]
    public async Task NoSecretsInOutput()
    {
        const string secret = "ha-secret-token";
        var previousUrl = Environment.GetEnvironmentVariable("HOMEASSISTANT_URL");
        var previousToken = Environment.GetEnvironmentVariable("HOMEASSISTANT_TOKEN");
        var previousEntity = Environment.GetEnvironmentVariable("HOMEASSISTANT_CLIMATE_ENTITY");
        Environment.SetEnvironmentVariable("HOMEASSISTANT_URL", "http://homeassistant.local:8123");
        Environment.SetEnvironmentVariable("HOMEASSISTANT_TOKEN", secret);
        Environment.SetEnvironmentVariable("HOMEASSISTANT_CLIMATE_ENTITY", "climate.test");
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var code = await ThermostatCli.RunAsync(["--live", "--dry-run", "--current-temp", "67", "--target-temp", "70"], output, error);

            Assert.Equal(0, code);
            Assert.DoesNotContain(secret, output.ToString());
            Assert.DoesNotContain(secret, error.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOMEASSISTANT_URL", previousUrl);
            Environment.SetEnvironmentVariable("HOMEASSISTANT_TOKEN", previousToken);
            Environment.SetEnvironmentVariable("HOMEASSISTANT_CLIMATE_ENTITY", previousEntity);
        }
    }

    [Fact]
    public async Task FakeCliUsesNoLiveHomeAssistantCallInTests()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await ThermostatCli.RunAsync(["--fake", "--current-temp", "67", "--target-temp", "70", "--ticks", "2"], output, error);

        Assert.Equal(0, code);
        Assert.Contains("Dominatus Thermostat Utility Controller", output.ToString());
        Assert.Empty(error.ToString());
    }
}
