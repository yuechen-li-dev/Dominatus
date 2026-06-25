using Godot;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dominatus.GodotTinyTown;

public partial class TinyTownMain : Node2D
{
    private static readonly LabelSettings DebugLabelSettings = CreateDebugLabelSettings();
    private static readonly LabelSettings SummaryLabelSettings = CreateSummaryLabelSettings();
    private static readonly LabelSettings TitleLabelSettings = CreateTitleLabelSettings();
    private static readonly LabelSettings NameplateLabelSettings = CreateNameplateLabelSettings();
    private static readonly StyleBoxFlat DebugPanelStyle = CreateDebugPanelStyle();
    private static readonly StyleBoxFlat NameplateStyle = CreateNameplateStyle();

    private const string SmokeEnv = "DOMINATUS_GODOT_SMOKE";
    private const string SmokeFramesEnv = "DOMINATUS_GODOT_SMOKE_FRAMES";
    private const string SmokeArtifactsEnv = "DOMINATUS_GODOT_SMOKE_ARTIFACTS";

    private TinyTownWorld? _world;
    private Label? _summaryLabel;
    private Label? _debugLabel;
    private Node? _villagersRoot;
    private Node? _destinationsRoot;
    private SmokeOptions? _smoke;
    private bool _smokeCompleted;
    private ulong _smokePhysicsFrames;
    private readonly Dictionary<string, HashSet<string>> _observedActivities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _observedPhases = new(StringComparer.Ordinal);
    private readonly HashSet<string> _observedTravelActivities = new(StringComparer.Ordinal);
    private readonly HashSet<string> _observedDwellActivities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, int>> _activityCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lastObservedState = new(StringComparer.Ordinal);

    public override void _Ready()
    {
        SetProcess(true);
        SetPhysicsProcess(true);
        _world = GetNode<TinyTownWorld>("DominatusWorld");
        _summaryLabel = GetNode<Label>("Hud/DebugPanel/Margin/VBox/SummaryLabel");
        _debugLabel = GetNode<Label>("Hud/DebugPanel/Margin/VBox/DebugLabel");
        _villagersRoot = GetNode<Node>("DominatusWorld/Villagers");
        _destinationsRoot = GetNode<Node>("DominatusWorld/Destinations");
        _smoke = SmokeOptions.TryCreate();

        ConfigureLayout();

        if (_smoke is null && DisplayServer.GetName().Contains("headless", StringComparison.OrdinalIgnoreCase))
        {
            var timer = GetTree().CreateTimer(2.0);
            timer.Timeout += () => GetTree().Quit();
        }
    }

    public override void _Process(double delta)
    {
        if (_world is null || _debugLabel is null || _summaryLabel is null || _villagersRoot is null)
            return;

        RecordObservedBehavior(_world);
        _summaryLabel.Text = BuildSummaryText(_world);
        _debugLabel.Text = BuildDebugText(_villagersRoot);
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

    private string BuildSummaryText(TinyTownWorld world)
    {
        var villagers = world.VillagerBrains.ToArray();
        var averageNeed = villagers.Length == 0
            ? 0f
            : villagers.Average(ComputeAverageNeed);
        var maxNeed = villagers.Length == 0
            ? 0f
            : villagers.Max(ComputeMaxNeed);

        return $"Tick {world.TicksProcessed}\n"
            + $"Agents {world.World.Agents.Count}\n"
            + "Need scale 0 calm -> 1 urgent\n"
            + $"Average need {averageNeed:0.00}\n"
            + $"Max need {maxNeed:0.00}\n"
            + $"Activities {FlattenObserved(_observedActivities).Length}\n"
            + $"Travel seen {JoinOrDash(_observedTravelActivities.Select(ShortActivityName))}\n"
            + $"Dwell seen {JoinOrDash(_observedDwellActivities.Select(ShortActivityName))}";
    }

    private static string BuildDebugText(Node villagersRoot)
    {
        var builder = new StringBuilder();

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
            _observedDwellActivities.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            _observedTravelActivities.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            world.VillagerBrains.Count == 0 ? 0f : world.VillagerBrains.Average(ComputeAverageNeed),
            world.VillagerBrains.Count == 0 ? 0f : world.VillagerBrains.Max(ComputeMaxNeed),
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
            ComputeMaxNeed(brain),
            OrderedObserved(_observedActivities, brain.VillagerName),
            OrderedObserved(_observedPhases, brain.VillagerName),
            OrderedActivityCounts(brain.VillagerName));
    }

    private void RecordObservedBehavior(TinyTownWorld world)
    {
        foreach (var brain in world.VillagerBrains)
        {
            var bb = brain.Bb;
            var activity = bb.GetOrDefault(TinyTownKeys.CurrentActivity, "Idle");
            var phase = bb.GetOrDefault(TinyTownKeys.CurrentPhase, "Choose");

            AddObserved(_observedActivities, brain.VillagerName, activity);
            AddObserved(_observedPhases, brain.VillagerName, phase);

            if (string.Equals(phase, "Travel", StringComparison.Ordinal))
                _observedTravelActivities.Add(activity);
            else if (string.Equals(phase, "Dwell", StringComparison.Ordinal))
                _observedDwellActivities.Add(activity);

            var state = $"{activity}|{phase}";
            if (_lastObservedState.TryGetValue(brain.VillagerName, out var previous) && string.Equals(previous, state, StringComparison.Ordinal))
                continue;

            _lastObservedState[brain.VillagerName] = state;
            AddActivityCount(brain.VillagerName, activity);
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

    private void AddActivityCount(string villagerName, string activity)
    {
        if (string.IsNullOrWhiteSpace(activity))
            return;

        if (!_activityCounts.TryGetValue(villagerName, out var counts))
        {
            counts = new Dictionary<string, int>(StringComparer.Ordinal);
            _activityCounts[villagerName] = counts;
        }

        counts.TryGetValue(activity, out var current);
        counts[activity] = current + 1;
    }

    private TinyTownActivityCount[] OrderedActivityCounts(string villagerName)
    {
        if (!_activityCounts.TryGetValue(villagerName, out var counts))
            return [];

        return counts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new TinyTownActivityCount(x.Key, x.Value))
            .ToArray();
    }

    private static string[] OrderedObserved(Dictionary<string, HashSet<string>> map, string villagerName)
    {
        return map.TryGetValue(villagerName, out var set)
            ? set.OrderBy(x => x, StringComparer.Ordinal).ToArray()
            : [];
    }

    private static string[] FlattenObserved(Dictionary<string, HashSet<string>> map)
        => map.Values.SelectMany(x => x).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static float ComputeAverageNeed(TinyTownVillagerBrain brain)
    {
        var bb = brain.Bb;
        return (bb.GetOrDefault(TinyTownKeys.Hunger, 0f)
            + bb.GetOrDefault(TinyTownKeys.Thirst, 0f)
            + bb.GetOrDefault(TinyTownKeys.RestNeed, 0f)
            + bb.GetOrDefault(TinyTownKeys.JoyNeed, 0f)
            + bb.GetOrDefault(TinyTownKeys.SocialNeed, 0f)) / 5f;
    }

    private static float ComputeMaxNeed(TinyTownVillagerBrain brain)
    {
        var bb = brain.Bb;
        return Math.Max(
            Math.Max(bb.GetOrDefault(TinyTownKeys.Hunger, 0f), bb.GetOrDefault(TinyTownKeys.Thirst, 0f)),
            Math.Max(
                Math.Max(bb.GetOrDefault(TinyTownKeys.RestNeed, 0f), bb.GetOrDefault(TinyTownKeys.JoyNeed, 0f)),
                bb.GetOrDefault(TinyTownKeys.SocialNeed, 0f)));
    }

    private static TinyTownVec2 ToVec2(Vector2 value) => new(value.X, value.Y);

    private static string GetGodotVersion()
    {
        var versionInfo = Engine.GetVersionInfo();
        if (versionInfo.TryGetValue("string", out var value))
            return value.AsString();

        return Engine.GetVersionInfo().ToString();
    }

    private static string JoinOrDash(IEnumerable<string> values)
    {
        var items = values.Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        return items.Length == 0 ? "-" : string.Join(", ", items);
    }

    private static string ShortActivityName(string activity)
    {
        return activity switch
        {
            "DrinkAtWell" => "Drink",
            "ShopAtMarket" => "Shop",
            "RestAtHome" => "Rest",
            "TendGarden" => "Garden",
            "Socialize" => "Social",
            "ReturnHome" => "Home",
            "Wander" => "Wander",
            "GoToWell" => "To Well",
            "GoToMarket" => "To Market",
            "Idle / Think" => "Idle",
            _ => activity
        };
    }

    private void ConfigureLayout()
    {
        if (GetWindow() is { } window)
            window.Size = TinyTownLayout.ViewportSize;

        var titleLabel = GetNode<Label>("Hud/DebugPanel/Margin/VBox/TitleLabel");
        titleLabel.LabelSettings = TitleLabelSettings;
        _summaryLabel!.LabelSettings = SummaryLabelSettings;
        _debugLabel!.LabelSettings = DebugLabelSettings;
        _summaryLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _debugLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        var debugPanel = GetNode<PanelContainer>("Hud/DebugPanel");
        debugPanel.Position = TinyTownLayout.DebugPanelRect.Position;
        debugPanel.Size = TinyTownLayout.DebugPanelRect.Size;
        debugPanel.AddThemeStyleboxOverride("panel", DebugPanelStyle);

        if (_destinationsRoot is null)
            return;

        foreach (var child in _destinationsRoot.GetChildren())
        {
            if (child is not Marker2D marker)
                continue;

            if (marker.GetNodeOrNull<PanelContainer>("Nameplate") is { } plate)
                plate.AddThemeStyleboxOverride("panel", NameplateStyle);

            if (marker.GetNodeOrNull<Label>("Nameplate/NameLabel") is { } label)
                label.LabelSettings = NameplateLabelSettings;
        }
    }

    private static LabelSettings CreateDebugLabelSettings()
    {
        return new LabelSettings
        {
            FontSize = 13,
            OutlineSize = 1,
            FontColor = new Color("efe6d3"),
            OutlineColor = new Color(0f, 0f, 0f, 0.22f)
        };
    }

    private static LabelSettings CreateSummaryLabelSettings()
    {
        return new LabelSettings
        {
            FontSize = 14,
            OutlineSize = 1,
            FontColor = new Color("fbf5ea"),
            OutlineColor = new Color(0f, 0f, 0f, 0.18f)
        };
    }

    private static LabelSettings CreateTitleLabelSettings()
    {
        return new LabelSettings
        {
            FontSize = 22,
            OutlineSize = 1,
            FontColor = new Color("fff8eb"),
            OutlineColor = new Color(0f, 0f, 0f, 0.12f)
        };
    }

    private static LabelSettings CreateNameplateLabelSettings()
    {
        return new LabelSettings
        {
            FontSize = 14,
            OutlineSize = 1,
            FontColor = new Color("2a251e"),
            OutlineColor = new Color(1f, 1f, 1f, 0.75f)
        };
    }

    private static StyleBoxFlat CreateDebugPanelStyle()
    {
        return new StyleBoxFlat
        {
            BgColor = new Color(0.13f, 0.14f, 0.16f, 0.92f),
            BorderColor = new Color(0.89f, 0.77f, 0.56f, 0.25f),
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12
        };
    }

    private static StyleBoxFlat CreateNameplateStyle()
    {
        return new StyleBoxFlat
        {
            BgColor = new Color(0.97f, 0.94f, 0.88f, 0.94f),
            BorderColor = new Color(0.38f, 0.29f, 0.20f, 0.20f),
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6
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
    string[] ObservedDwellActivities,
    string[] ObservedTravelActivities,
    float AverageNeedUrgency,
    float MaxNeedUrgency,
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
    float MaxNeed,
    string[] ObservedActivities,
    string[] ObservedPhases,
    TinyTownActivityCount[] ActivityCounts);

public sealed record TinyTownVec2(float X, float Y);

public sealed record TinyTownActivityCount(string Activity, int Count);

[JsonSerializable(typeof(TinyTownDebugSnapshot))]
internal partial class SmokeJsonContext : JsonSerializerContext
{
}
