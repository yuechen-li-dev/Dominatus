using Godot;

namespace Dominatus.GodotTinyTown;

public partial class VillagerActor : CharacterBody2D
{
    private static readonly LabelSettings SharedLabelSettings = CreateLabelSettings();
    private static readonly StyleBoxFlat StatusPlateStyle = CreateStatusPlateStyle();

    private TinyTownVillagerBrain? _brain;
    private Node2D? _visualRoot;
    private Polygon2D? _placeholderBody;
    private PanelContainer? _statusPlate;
    private Label? _label;
    private string _villagerName = "Villager";
    private double _labelRefreshAccumulator;
    private string _lastLabelText = string.Empty;

    public override void _Ready()
    {
        _brain = GetNodeOrNull<TinyTownVillagerBrain>("Brain");
        _visualRoot = GetNodeOrNull<Node2D>("VisualRoot");
        _placeholderBody = GetNodeOrNull<Polygon2D>("VisualRoot/Body");
        _statusPlate = GetNodeOrNull<PanelContainer>("StatusPlate");
        _label = GetNodeOrNull<Label>("StatusLabel");

        if (_brain is not null)
            _villagerName = _brain.VillagerName;

        if (_statusPlate is not null)
            _statusPlate.AddThemeStyleboxOverride("panel", StatusPlateStyle);

        if (_label is not null)
        {
            _label.LabelSettings = SharedLabelSettings;
            _label.HorizontalAlignment = HorizontalAlignment.Center;
            _label.Position = TinyTownLayout.VillagerLabelPadding;
        }
    }

    public override void _Process(double delta)
    {
        if (_brain?.Agent is null || _label is null || _placeholderBody is null || _statusPlate is null)
            return;

        var bb = _brain.Bb;
        var activity = bb.GetOrDefault(TinyTownKeys.CurrentActivity, "Idle");
        var phase = bb.GetOrDefault(TinyTownKeys.CurrentPhase, "Choose");
        _labelRefreshAccumulator += delta;
        if (_labelRefreshAccumulator >= 0.10d || _lastLabelText.Length == 0)
        {
            _labelRefreshAccumulator = 0d;
            _lastLabelText = $"{_villagerName}\n{ShortActivity(activity)} · {phase}\n{NeedsLine(bb)}";
            _label.Text = _lastLabelText;
            var labelSize = _label.GetMinimumSize();
            _statusPlate.Size = labelSize + (TinyTownLayout.VillagerLabelPadding * 2f);
        }

        _statusPlate.Position = ComputePlateOffset();
        _placeholderBody.Color = ActivityColor(activity);

        // Future sprite-sheet work can replace the placeholder body under VisualRoot
        // without changing the Dominatus brain or status plate behavior.
        _visualRoot ??= GetNodeOrNull<Node2D>("VisualRoot");
    }

    public string BuildDebugSummary()
    {
        if (_brain?.Agent is null)
            return $"{Name}: not ready";

        var bb = _brain.Bb;
        return $"{_villagerName,-4} {ShortPersonality(bb.GetOrDefault(TinyTownKeys.PersonalityName, "Villager")),-8} "
            + $"{ShortActivity(bb.GetOrDefault(TinyTownKeys.CurrentActivity, "Idle")),-8}/{ShortPhase(bb.GetOrDefault(TinyTownKeys.CurrentPhase, "Choose")),-3} "
            + $"{NeedsLine(bb)}";
    }

    private static Color ActivityColor(string activity) => activity switch
    {
        "GoToWell" => new Color("5f9ecf"),
        "DrinkAtWell" => new Color("2e6f95"),
        "GoToMarket" => new Color("eea04f"),
        "ShopAtMarket" => new Color("cb6d2e"),
        "RestAtHome" => new Color("8a6c52"),
        "ReturnHome" => new Color("9f8264"),
        "TendGarden" => new Color("4f8b57"),
        "Wander" => new Color("6c8da6"),
        "Socialize" => new Color("b45f7b"),
        "Idle / Think" => new Color("7b8ea2"),
        _ => new Color("7f8f70")
    };

    private static string NeedsLine(Dominatus.Core.Blackboard.Blackboard bb)
        => $"H{Pct(bb.GetOrDefault(TinyTownKeys.Hunger, 0f))} "
        + $"T{Pct(bb.GetOrDefault(TinyTownKeys.Thirst, 0f))} "
        + $"R{Pct(bb.GetOrDefault(TinyTownKeys.RestNeed, 0f))} "
        + $"J{Pct(bb.GetOrDefault(TinyTownKeys.JoyNeed, 0f))} "
        + $"S{Pct(bb.GetOrDefault(TinyTownKeys.SocialNeed, 0f))}";

    private static string ShortActivity(string activity)
    {
        return activity switch
        {
            "GoToWell" => "To Well",
            "DrinkAtWell" => "Drink",
            "GoToMarket" => "To Market",
            "ShopAtMarket" => "Shop",
            "RestAtHome" => "Rest",
            "ReturnHome" => "Homeward",
            "TendGarden" => "Garden",
            "Wander" => "Wander",
            "Socialize" => "Social",
            "Idle / Think" => "Idle",
            _ => activity
        };
    }

    private static string ShortPhase(string phase)
        => phase switch
        {
            "Travel" => "Trv",
            "Dwell" => "Dwl",
            "Choose" => "Chs",
            _ => phase
        };

    private static LabelSettings CreateLabelSettings()
    {
        return new LabelSettings
        {
            FontSize = 12,
            OutlineSize = 1,
            FontColor = new Color("f7f4ee"),
            OutlineColor = new Color(0f, 0f, 0f, 0.16f)
        };
    }

    private static StyleBoxFlat CreateStatusPlateStyle()
    {
        return new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.11f, 0.10f, 0.82f),
            BorderColor = new Color(1f, 1f, 1f, 0.12f),
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

    private static string ShortPersonality(string personality)
        => personality switch
        {
            "Social shopper" => "Shopper",
            "Restless wanderer" => "Wanderer",
            "Quiet gardener" => "Gardener",
            "Cozy homebody" => "Homebody",
            _ => personality
        };

    private static int Pct(float value) => (int)MathF.Round(Math.Clamp(value, 0f, 1f) * 100f);

    private Vector2 ComputePlateOffset()
    {
        var offset = TinyTownLayout.VillagerLabelOffset;
        if (GlobalPosition.X < 250f)
            offset.X += 36f;

        if (GlobalPosition.Y > 470f)
            offset.Y -= 10f;

        return offset;
    }
}
