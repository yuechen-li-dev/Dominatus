using Godot;

namespace Dominatus.GodotTinyTown;

public partial class VillagerActor : CharacterBody2D
{
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
    }

    public override void _Process(double delta)
    {
        if (_brain?.Agent is null || _label is null || _visual is null)
            return;

        var bb = _brain.Bb;
        var activity = bb.GetOrDefault(TinyTownKeys.CurrentActivity, "Idle");
        var need = bb.GetOrDefault(TinyTownKeys.CurrentNeed, "Settling in");

        _label.Text = $"{_villagerName}\n{activity}\nneed: {need}";
        _label.Position = new Vector2(-26f, -42f);
        _visual.Color = ActivityColor(activity);
    }

    public string BuildDebugSummary()
    {
        if (_brain?.Agent is null)
            return $"{Name}: not ready";

        var bb = _brain.Bb;
        return $"{_villagerName}: {bb.GetOrDefault(TinyTownKeys.CurrentActivity, "Idle")} "
            + $"H {bb.GetOrDefault(TinyTownKeys.Hunger, 0f):0.00} "
            + $"T {bb.GetOrDefault(TinyTownKeys.Thirst, 0f):0.00} "
            + $"E {bb.GetOrDefault(TinyTownKeys.Energy, 0f):0.00} "
            + $"J {bb.GetOrDefault(TinyTownKeys.GardenJoy, 0f):0.00}";
    }

    private static Color ActivityColor(string activity) => activity switch
    {
        "GoToWell" => new Color("6fa8dc"),
        "GoToMarket" => new Color("f6b26b"),
        "RestAtHome" => new Color("93c47d"),
        "TendGarden" => new Color("76a5af"),
        "Wander" => new Color("d5a6bd"),
        _ => new Color("d9d2e9")
    };
}
