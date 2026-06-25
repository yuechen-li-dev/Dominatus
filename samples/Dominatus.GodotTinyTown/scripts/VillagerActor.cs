using Godot;

namespace Dominatus.GodotTinyTown;

public partial class VillagerActor : CharacterBody2D
{
    private static readonly LabelSettings SharedLabelSettings = CreateLabelSettings();
    private static readonly StyleBoxFlat StatusPlateStyle = CreateStatusPlateStyle();

    private TinyTownVillagerBrain? _brain;
    private Node2D? _visualRoot;
    private PanelContainer? _statusPlate;
    private Label? _label;
    private VillagerVisualController? _visualController;
    private string _villagerName = "Villager";
    private double _labelRefreshAccumulator;
    private string _lastLabelText = string.Empty;
    private TinyTownFacingDirection _lastFacingDirection = TinyTownFacingDirection.Down;
    private Vector2 _lastFacing = Vector2.Down;
    private TinyTownVillagerPresentation? _lastPresentation;

    public override void _Ready()
    {
        _brain = GetNodeOrNull<TinyTownVillagerBrain>("Brain");
        _visualRoot = GetNodeOrNull<Node2D>("VisualRoot");
        _statusPlate = GetNodeOrNull<PanelContainer>("StatusPlate");
        _label = GetNodeOrNull<Label>("StatusPlate/StatusLabel");

        if (_brain is not null)
            _villagerName = _brain.VillagerName;

        if (_visualRoot is not null)
            _visualController = new VillagerVisualController(_visualRoot);

        if (_statusPlate is not null)
            _statusPlate.AddThemeStyleboxOverride("panel", StatusPlateStyle);

        if (_label is not null)
        {
            _label.LabelSettings = SharedLabelSettings;
            _label.HorizontalAlignment = HorizontalAlignment.Center;
            _label.Position = TinyTownLayout.VillagerLabelPadding;
        }
    }

    public TinyTownVisualStatus VisualStatus => _visualController?.Status
        ?? new TinyTownVisualStatus(TinyTownVisualMode.FallbackShapes, TinyTownVisualMode.FallbackShapes, true, false);

    public TinyTownVillagerPresentation? LastPresentation => _lastPresentation;

    public void ConfigureVisuals(TinyTownArtProfile profile, TinyTownSpriteCatalog catalog)
        => _visualController?.Configure(profile, catalog);

    public override void _Process(double delta)
    {
        if (_brain?.Agent is null || _label is null || _statusPlate is null)
            return;

        var bb = _brain.Bb;
        var activity = bb.GetOrDefault(TinyTownKeys.CurrentActivity, "Idle");
        var phase = bb.GetOrDefault(TinyTownKeys.CurrentPhase, "Choose");
        var presentation = BuildPresentation(bb, activity, phase);
        _lastPresentation = presentation;

        _labelRefreshAccumulator += delta;
        if (_labelRefreshAccumulator >= 0.10d || _lastLabelText.Length == 0)
        {
            _labelRefreshAccumulator = 0d;
            _lastLabelText = BuildStatusLabel(activity, phase, presentation);
            _label.Text = _lastLabelText;
            var labelSize = _label.GetMinimumSize();
            _statusPlate.Size = labelSize + (TinyTownLayout.VillagerLabelPadding * 2f);
        }

        _statusPlate.Position = ComputePlateOffset();
        _visualController?.Apply(presentation);
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

    private static string NeedsLine(Dominatus.Core.Blackboard.Blackboard bb)
        => $"H{Pct(bb.GetOrDefault(TinyTownKeys.Hunger, 0f))} "
        + $"T{Pct(bb.GetOrDefault(TinyTownKeys.Thirst, 0f))} "
        + $"R{Pct(bb.GetOrDefault(TinyTownKeys.RestNeed, 0f))} "
        + $"J{Pct(bb.GetOrDefault(TinyTownKeys.JoyNeed, 0f))} "
        + $"S{Pct(bb.GetOrDefault(TinyTownKeys.SocialNeed, 0f))}";

    private static string NeedsLine(TinyTownVillagerPresentation presentation)
        => $"H{Pct(presentation.Hunger)} "
        + $"T{Pct(presentation.Thirst)} "
        + $"R{Pct(presentation.Rest)} "
        + $"J{Pct(presentation.Joy)} "
        + $"S{Pct(presentation.Social)}";

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

    private string BuildStatusLabel(string activity, string phase, TinyTownVillagerPresentation presentation)
    {
        var label = $"{_villagerName}\n{ShortActivity(activity)} · {phase}\n{NeedsLine(presentation)}";
        if (_brain is null)
            return label;

        var visibleUntil = _brain.Bb.GetOrDefault(TinyTownKeys.BarkVisibleUntil, 0f);
        var barkText = _brain.Bb.GetOrDefault(TinyTownKeys.LastBarkText, string.Empty);
        if (visibleUntil > _brain.World.Clock.Time && !string.IsNullOrWhiteSpace(barkText))
            label += $"\n\"{barkText}\"";

        return label;
    }

    private TinyTownVillagerPresentation BuildPresentation(
        Dominatus.Core.Blackboard.Blackboard bb,
        string activity,
        string phase)
    {
        var speed = Velocity.Length();
        var facing = speed > 4f ? Velocity.Normalized() : _lastFacing;
        var facingDirection = speed > 4f ? DetermineFacingDirection(Velocity) : _lastFacingDirection;

        _lastFacing = facing;
        _lastFacingDirection = facingDirection;

        return new TinyTownVillagerPresentation
        {
            Name = _villagerName,
            Personality = bb.GetOrDefault(TinyTownKeys.PersonalityName, "Villager"),
            Activity = activity,
            Phase = phase,
            Velocity = Velocity,
            Facing = facing,
            FacingDirection = facingDirection,
            Speed = speed,
            Hunger = bb.GetOrDefault(TinyTownKeys.Hunger, 0f),
            Thirst = bb.GetOrDefault(TinyTownKeys.Thirst, 0f),
            Rest = bb.GetOrDefault(TinyTownKeys.RestNeed, 0f),
            Joy = bb.GetOrDefault(TinyTownKeys.JoyNeed, 0f),
            Social = bb.GetOrDefault(TinyTownKeys.SocialNeed, 0f)
        };
    }

    private static TinyTownFacingDirection DetermineFacingDirection(Vector2 velocity)
    {
        if (MathF.Abs(velocity.X) > MathF.Abs(velocity.Y))
            return velocity.X < 0f ? TinyTownFacingDirection.Left : TinyTownFacingDirection.Right;

        return velocity.Y < 0f ? TinyTownFacingDirection.Up : TinyTownFacingDirection.Down;
    }

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
