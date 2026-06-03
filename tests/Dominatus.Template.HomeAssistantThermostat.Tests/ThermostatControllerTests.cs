using Dominatus.Template.HomeAssistantThermostat;

public sealed class ThermostatControllerTests
{
    [Fact]
    public async Task Thermostat_UsesAiDecideForModeSelection()
    {
        var result = await RunFakeAsync([new ThermostatTickInput(67, 70)], minCommit: 0);

        Assert.True(result.Metadata.UsedAiWorld);
        Assert.True(result.Metadata.UsedAiAgent);
        Assert.True(result.Metadata.UsedHfsm);
        Assert.True(result.Metadata.UsedAiDecide);
        Assert.Equal("thermostat.mode", result.Metadata.DecisionSlot);
        Assert.NotEmpty(result.Metadata.DecisionReports);
    }

    [Fact]
    public async Task Thermostat_UsesDecisionPolicyForHysteresisAndMinCommit()
    {
        var result = await RunFakeAsync([
            new ThermostatTickInput(67, 70),
            new ThermostatTickInput(70.1, 70),
            new ThermostatTickInput(74, 70)
        ], hysteresis: 0.5, minCommit: 3);

        Assert.True(result.Metadata.UsedDecisionPolicy);
        Assert.Contains(result.Decisions, decision => decision.DecisionReason is "MinCommitActive" or "HysteresisBlock");
    }

    [Fact]
    public async Task Thermostat_HeatBelowTarget_EmitsAiActCommand()
    {
        var actuator = new FakeHomeAssistantThermostatActuator();
        var workflow = new ThermostatWorkflow(actuator, "climate.living_room", new ThermostatPolicy(0.5, 0));

        var result = await workflow.RunAsync([new ThermostatTickInput(67, 70)], 0.5);

        var command = Assert.Single(result.Commands);
        Assert.Equal("climate.living_room", command.EntityId);
        Assert.Equal("heat", command.HvacMode);
        Assert.Single(actuator.Commands);
        Assert.True(result.Metadata.UsedAiDecide);
    }

    [Fact]
    public async Task Thermostat_CoolAboveTarget_EmitsAiActCommand()
    {
        var result = await RunFakeAsync([new ThermostatTickInput(74, 70)], minCommit: 0);

        var command = Assert.Single(result.Commands);
        Assert.Equal(ThermostatMode.Cool, command.Mode);
        Assert.Equal("cool", command.HvacMode);
    }

    [Fact]
    public async Task Thermostat_WithinDeadband_HoldsOrIdles()
    {
        var result = await RunFakeAsync([new ThermostatTickInput(70.2, 70)], minCommit: 0);

        Assert.Equal(ThermostatMode.Idle, result.Decisions.Single().CommittedMode);
        Assert.Empty(result.Commands);
    }

    [Fact]
    public async Task Thermostat_MinCommit_PreventsThrashingAcrossTicks()
    {
        var result = await RunFakeAsync([
            new ThermostatTickInput(67, 70),
            new ThermostatTickInput(74, 70),
            new ThermostatTickInput(67, 70)
        ], hysteresis: 0, minCommit: 3);

        Assert.Single(result.Commands);
        Assert.All(result.Decisions, decision => Assert.Equal(ThermostatMode.Heat, decision.CommittedMode));
        Assert.Contains(result.Decisions, decision => string.Equals(decision.DecisionReason, "MinCommitActive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Thermostat_FakeMode_NoNetwork()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await ThermostatCli.RunAsync(["--fake", "--current-temp", "67", "--target-temp", "70", "--ticks", "2"], output, error);

        Assert.Equal(0, code);
        Assert.Contains("Dominatus Thermostat Utility Controller", output.ToString());
        Assert.Contains("Ai.Decide=True", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Thermostat_LiveMode_RequiresEnvVars()
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
    public async Task Thermostat_DryRun_DoesNotCallNetwork()
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
    public void StaticGuard_UsesDominatusPrimitivesInsteadOfManualDecisionLoop()
    {
        var controllerSource = File.ReadAllText(Path.Combine(RepoRoot(), "samples/Templates/Dominatus.Template.HomeAssistantThermostat/ThermostatController.cs"));
        var workflowSource = File.ReadAllText(Path.Combine(RepoRoot(), "samples/Templates/Dominatus.Template.HomeAssistantThermostat/ThermostatWorkflow.cs"));
        var combined = controllerSource + workflowSource;

        Assert.DoesNotContain(".Decide(", workflowSource);
        Assert.DoesNotContain("ScoreHeat", combined);
        Assert.DoesNotContain("ChooseDesired", combined);
        Assert.Contains("Consideration", combined);
        Assert.Contains("Ai.Option", combined);
        Assert.Contains("Ai.Decide", combined);
        Assert.Contains("DecisionPolicy", combined);
        Assert.Contains("Ai.Act", combined);
    }

    private static Task<ThermostatRunResult> RunFakeAsync(IReadOnlyList<ThermostatTickInput> ticks, double hysteresis = 0.5, int minCommit = 0)
    {
        var actuator = new FakeHomeAssistantThermostatActuator();
        var workflow = new ThermostatWorkflow(actuator, "climate.test", new ThermostatPolicy(hysteresis, minCommit));
        return workflow.RunAsync(ticks, 0.5);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dominatus.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repo root.");
    }
}
