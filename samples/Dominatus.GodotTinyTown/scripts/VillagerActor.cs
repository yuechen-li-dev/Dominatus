using Godot;

namespace Dominatus.GodotTinyTown;

public partial class VillagerActor : CharacterBody2D
{
    private static readonly LabelSettings SharedLabelSettings = CreateLabelSettings();

    private TinyTownVillagerBrain? _brain;
    private Polygon2D? _visual;
    private Label? _label;
    private string _villagerName = "Villager";

    public override void _Ready()
    {
        _brain = GetNodeOrNull<TinyTownVillagerBrain>("Brain");
        _visual = GetNodeOrNull<Polygon2D>("Visual");
        _label = GetNodeOrNull<Label>("StatusLabel");

        if (_brain is not null)
            _villagerName = _brain.VillagerName;

        if (_label is not null)
        {
            _label.LabelSettings = SharedLabelSettings;
            _label.HorizontalAlignment = HorizontalAlignment.Center;
        }
    }

    public override void _Process(double delta)
    {
        if (_brain?.Agent is null || _label is null || _visual is null)
            return;

        var bb = _brain.Bb;
        var activity = bb.GetOrDefault(TinyTownKeys.CurrentActivity, "Idle");
        var need = bb.GetOrDefault(TinyTownKeys.CurrentNeed, "Settling in");
        var phase = bb.GetOrDefault(TinyTownKeys.CurrentPhase, "Choose");
        var personality = bb.GetOrDefault(TinyTownKeys.PersonalityName, "Villager");

        _label.Text = $"{_villagerName} [{personality}]\n{activity} [{phase}] {need}\n{NeedsLine(bb)}";
        _label.Position = LabelOffset(_villagerName);
        _visual.Color = ActivityColor(activity);
    }

    public string BuildDebugSummary()
    {
        if (_brain?.Agent is null)
            return $"{Name}: not ready";

        var bb = _brain.Bb;
        return $"{_villagerName} [{bb.GetOrDefault(TinyTownKeys.PersonalityName, "Villager")}]: "
            + $"{bb.GetOrDefault(TinyTownKeys.CurrentActivity, "Idle")} "
            + $"[{bb.GetOrDefault(TinyTownKeys.CurrentPhase, "Choose")}] "
            + $"H {bb.GetOrDefault(TinyTownKeys.Hunger, 0f):0.00} "
            + $"T {bb.GetOrDefault(TinyTownKeys.Thirst, 0f):0.00} "
            + $"R {bb.GetOrDefault(TinyTownKeys.RestNeed, 0f):0.00} "
            + $"J {bb.GetOrDefault(TinyTownKeys.JoyNeed, 0f):0.00} "
            + $"S {bb.GetOrDefault(TinyTownKeys.SocialNeed, 0f):0.00} "
            + $"win {bb.GetOrDefault(TinyTownKeys.LastDecisionWinner, "-")}";
    }

    private static Color ActivityColor(string activity) => activity switch
    {
        "GoToWell" => new Color("6fa8dc"),
        "DrinkAtWell" => new Color("3d85c6"),
        "GoToMarket" => new Color("f6b26b"),
        "ShopAtMarket" => new Color("e69138"),
        "RestAtHome" => new Color("93c47d"),
        "ReturnHome" => new Color("b6d7a8"),
        "TendGarden" => new Color("76a5af"),
        "Wander" => new Color("d5a6bd"),
        "Socialize" => new Color("c27ba0"),
        "Idle / Think" => new Color("c9daf8"),
        _ => new Color("d9d2e9")
    };

    private static string NeedsLine(Dominatus.Core.Blackboard.Blackboard bb)
        => $"H {bb.GetOrDefault(TinyTownKeys.Hunger, 0f):0.00}  "
        + $"T {bb.GetOrDefault(TinyTownKeys.Thirst, 0f):0.00}  "
        + $"R {bb.GetOrDefault(TinyTownKeys.RestNeed, 0f):0.00}  "
        + $"J {bb.GetOrDefault(TinyTownKeys.JoyNeed, 0f):0.00}  "
        + $"S {bb.GetOrDefault(TinyTownKeys.SocialNeed, 0f):0.00}";

    private static Vector2 LabelOffset(string villagerName)
    {
        var x = villagerName switch
        {
            "Maya" => -34f,
            "Theo" => 8f,
            "Lina" => -16f,
            "Nia" => 20f,
            _ => 0f
        };

        return new Vector2(x, -70f);
    }

    private static LabelSettings CreateLabelSettings()
    {
        return new LabelSettings
        {
            FontSize = 11,
            OutlineSize = 2,
            FontColor = new Color("1f1b16"),
            OutlineColor = new Color(1f, 1f, 1f, 0.92f)
        };
    }
}
