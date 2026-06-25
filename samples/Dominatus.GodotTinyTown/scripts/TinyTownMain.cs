using Godot;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dominatus.GodotTinyTown;

public partial class TinyTownMain : Node2D
{
    private static readonly LabelSettings DebugLabelSettings = CreateDebugLabelSettings();

    private const string SmokeEnv = "DOMINATUS_GODOT_SMOKE";
    private const string SmokeFramesEnv = "DOMINATUS_GODOT_SMOKE_FRAMES";
    private const string SmokeArtifactsEnv = "DOMINATUS_GODOT_SMOKE_ARTIFACTS";

    private TinyTownWorld? _world;
    private Label? _debugLabel;
    private Node? _villagersRoot;
    private SmokeOptions? _smoke;
    private bool _smokeCompleted;
    private ulong _smokePhysicsFrames;
    private readonly Dictionary<string, HashSet<string>> _observedActivities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _observedPhases = new(StringComparer.Ordinal);

    public override void _Ready()
    {
        SetProcess(true);
        SetPhysicsProcess(true);
        _world = GetNode<TinyTownWorld>("DominatusWorld");
        _debugLabel = GetNode<Label>("DebugLabel");
        _villagersRoot = GetNode<Node>("DominatusWorld/Villagers");
        _smoke = SmokeOptions.TryCreate();
        _debugLabel.LabelSettings = DebugLabelSettings;

        if (_smoke is null && DisplayServer.GetName().Contains("headless", StringComparison.OrdinalIgnoreCase))
        {
            var timer = GetTree().CreateTimer(2.0);
            timer.Timeout += () => GetTree().Quit();
        }
    }

    public override void _Process(double delta)
    {
        if (_world is null || _debugLabel is null || _villagersRoot is null)
            return;

        RecordObservedBehavior(_world);
        _debugLabel.Text = BuildDebugText(_world, _villagersRoot);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_world is null || _smoke is null || _smokeCompleted)
            return;

        _smokePhysicsFrames++;
        if (_smokePhysicsFrames < _smoke.TargetTicks)
            return;

        _smokeCompleted = true;
        FinalizeSmokeRun(_smoke, _world);
    }

    private static string BuildDebugText(TinyTownWorld world, Node villagersRoot)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"DominatusWorld ticks: {world.TicksProcessed}");
        builder.AppendLine($"Agents: {world.World.Agents.Count}");
        builder.AppendLine("Need scale: 0 = calm, 1 = urgent");

        foreach (var child in villagersRoot.GetChildren())
        {
            if (child is VillagerActor actor)
                builder.AppendLine(actor.BuildDebugSummary());
        }

        return builder.ToString().TrimEnd();
    }

    private void FinalizeSmokeRun(SmokeOptions smoke, TinyTownWorld world)
    {
        Directory.CreateDirectory(smoke.ArtifactsDir);

        var screenshotPath = Path.Combine(smoke.ArtifactsDir, "tinytown-screenshot.png");
        var screenshotSaved = TrySaveScreenshot(screenshotPath, out var screenshotError);

        var snapshot = CreateSnapshot(world, screenshotPath, screenshotSaved, screenshotError);
        var jsonPath = Path.Combine(smoke.ArtifactsDir, "tinytown-debug.json");
        var json = JsonSerializer.Serialize(snapshot, SmokeJsonContext.Default.TinyTownDebugSnapshot);
        File.WriteAllText(jsonPath, json + System.Environment.NewLine);

        GD.Print($"Dominatus smoke artifacts written to: {smoke.ArtifactsDir}");
        GetTree().Quit();
    }

    private TinyTownDebugSnapshot CreateSnapshot(
        TinyTownWorld world,
        string screenshotPath,
        bool screenshotSaved,
        string? screenshotError)
    {
        var villagers = world.VillagerBrains
            .Select(CreateVillagerSnapshot)
            .OrderBy(v => v.Name, StringComparer.Ordinal)
            .ToArray();

        return new TinyTownDebugSnapshot(
            GetGodotVersion(),
            world.TicksProcessed,
            world.World.Agents.Count,
            screenshotSaved,
            screenshotPath,
            screenshotError,
            FlattenObserved(_observedActivities),
            villagers);
    }

    private TinyTownVillagerSnapshot CreateVillagerSnapshot(TinyTownVillagerBrain brain)
    {
        var body = brain.GetParent() as CharacterBody2D
            ?? throw new InvalidOperationException("TinyTownVillagerBrain expects to be attached under a CharacterBody2D.");
        var bb = brain.Bb;
        var initialPosition = bb.GetOrDefault(TinyTownKeys.InitialPosition, body.GlobalPosition);
        var position = body.GlobalPosition;

        return new TinyTownVillagerSnapshot(
            brain.VillagerName,
            bb.GetOrDefault(TinyTownKeys.PersonalityName, "Villager"),
            bb.GetOrDefault(TinyTownKeys.CurrentActivity, "Idle"),
            bb.GetOrDefault(TinyTownKeys.CurrentIntent, string.Empty),
            bb.GetOrDefault(TinyTownKeys.CurrentPhase, "Choose"),
            bb.GetOrDefault(TinyTownKeys.CurrentNeed, "Unknown"),
            bb.GetOrDefault(TinyTownKeys.CurrentTargetKind, "None"),
            bb.GetOrDefault(TinyTownKeys.LastDecisionWinner, string.Empty),
            bb.GetOrDefault(TinyTownKeys.LastDecisionScore, 0f),
            bb.GetOrDefault(TinyTownKeys.ActivityRemainingSeconds, 0f),
            ToVec2(position),
            ToVec2(initialPosition),
            ToVec2(bb.GetOrDefault(TinyTownKeys.HomePosition, Vector2.Zero)),
            ToVec2(bb.GetOrDefault(TinyTownKeys.CurrentTargetPosition, Vector2.Zero)),
            position.DistanceTo(initialPosition),
            bb.GetOrDefault(TinyTownKeys.Hunger, 0f),
            bb.GetOrDefault(TinyTownKeys.Thirst, 0f),
            bb.GetOrDefault(TinyTownKeys.RestNeed, 0f),
            bb.GetOrDefault(TinyTownKeys.JoyNeed, 0f),
            bb.GetOrDefault(TinyTownKeys.SocialNeed, 0f),
            OrderedObserved(_observedActivities, brain.VillagerName),
            OrderedObserved(_observedPhases, brain.VillagerName));
    }

    private void RecordObservedBehavior(TinyTownWorld world)
    {
        foreach (var brain in world.VillagerBrains)
        {
            var bb = brain.Bb;
            AddObserved(_observedActivities, brain.VillagerName, bb.GetOrDefault(TinyTownKeys.CurrentActivity, "Idle"));
            AddObserved(_observedPhases, brain.VillagerName, bb.GetOrDefault(TinyTownKeys.CurrentPhase, "Choose"));
        }
    }

    private static void AddObserved(Dictionary<string, HashSet<string>> map, string villagerName, string value)
    {
        if (!map.TryGetValue(villagerName, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            map[villagerName] = set;
        }

        if (!string.IsNullOrWhiteSpace(value))
            set.Add(value);
    }

    private static string[] OrderedObserved(Dictionary<string, HashSet<string>> map, string villagerName)
    {
        return map.TryGetValue(villagerName, out var set)
            ? set.OrderBy(x => x, StringComparer.Ordinal).ToArray()
            : [];
    }

    private static string[] FlattenObserved(Dictionary<string, HashSet<string>> map)
        => map.Values.SelectMany(x => x).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static TinyTownVec2 ToVec2(Vector2 value) => new(value.X, value.Y);

    private static string GetGodotVersion()
    {
        var versionInfo = Engine.GetVersionInfo();
        if (versionInfo.TryGetValue("string", out var value))
            return value.AsString();

        return Engine.GetVersionInfo().ToString();
    }

    private static LabelSettings CreateDebugLabelSettings()
    {
        return new LabelSettings
        {
            FontSize = 13,
            OutlineSize = 2,
            FontColor = new Color("201a13"),
            OutlineColor = new Color(1f, 1f, 1f, 0.88f)
        };
    }

    private bool TrySaveScreenshot(string path, out string? error)
    {
        if (DisplayServer.GetName().Contains("headless", StringComparison.OrdinalIgnoreCase))
        {
            error = "Headless renderer does not expose a viewport screenshot texture.";
            return false;
        }

        try
        {
            var texture = GetViewport().GetTexture();
            if (texture is null)
            {
                error = "Viewport texture was null.";
                return false;
            }

            using var image = texture.GetImage();
            var result = image.SavePng(path);
            if (result == Error.Ok)
            {
                error = null;
                return true;
            }

            error = $"SavePng failed with {result}.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private sealed record SmokeOptions(string ArtifactsDir, ulong TargetTicks)
    {
        public static SmokeOptions? TryCreate()
        {
            var rawEnabled = System.Environment.GetEnvironmentVariable(SmokeEnv);
            if (!IsEnabled(rawEnabled))
                return null;

            var rawTicks = System.Environment.GetEnvironmentVariable(SmokeFramesEnv);
            var targetTicks = 120UL;
            if (!string.IsNullOrWhiteSpace(rawTicks)
                && ulong.TryParse(rawTicks, out var parsedTicks)
                && parsedTicks > 0)
            {
                targetTicks = parsedTicks;
            }

            var projectDir = ProjectSettings.GlobalizePath("res://");
            var artifactsDir = System.Environment.GetEnvironmentVariable(SmokeArtifactsEnv);
            if (string.IsNullOrWhiteSpace(artifactsDir))
            {
                artifactsDir = Path.GetFullPath(Path.Combine(projectDir, "..", "..", "artifacts", "godot-tinytown"));
            }

            return new SmokeOptions(Path.GetFullPath(artifactsDir), targetTicks);
        }

        private static bool IsEnabled(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && (value == "1"
                    || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
        }
    }
}

public sealed record TinyTownDebugSnapshot(
    string GodotVersion,
    ulong TickCount,
    int AgentCount,
    bool ScreenshotSaved,
    string ScreenshotPath,
    string? ScreenshotError,
    string[] ObservedActivities,
    TinyTownVillagerSnapshot[] Villagers);

public sealed record TinyTownVillagerSnapshot(
    string Name,
    string Personality,
    string Activity,
    string Intent,
    string Phase,
    string Need,
    string TargetKind,
    string LastDecisionWinner,
    float LastDecisionScore,
    float ActivityRemainingSeconds,
    TinyTownVec2 Position,
    TinyTownVec2 InitialPosition,
    TinyTownVec2 HomePosition,
    TinyTownVec2 TargetPosition,
    float DistanceFromInitialPosition,
    float Hunger,
    float Thirst,
    float RestNeed,
    float JoyNeed,
    float SocialNeed,
    string[] ObservedActivities,
    string[] ObservedPhases);

public sealed record TinyTownVec2(float X, float Y);

[JsonSerializable(typeof(TinyTownDebugSnapshot))]
internal partial class SmokeJsonContext : JsonSerializerContext
{
}
