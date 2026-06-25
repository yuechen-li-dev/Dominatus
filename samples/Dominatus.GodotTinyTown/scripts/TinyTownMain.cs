using Godot;
using System.Text;

namespace Dominatus.GodotTinyTown;

public partial class TinyTownMain : Node2D
{
    private TinyTownWorld? _world;
    private Label? _debugLabel;
    private Node? _villagersRoot;

    public override void _Ready()
    {
        _world = GetNode<TinyTownWorld>("DominatusWorld");
        _debugLabel = GetNode<Label>("DebugLabel");
        _villagersRoot = GetNode<Node>("DominatusWorld/Villagers");

        if (DisplayServer.GetName().Contains("headless", StringComparison.OrdinalIgnoreCase))
        {
            var timer = GetTree().CreateTimer(2.0);
            timer.Timeout += () => GetTree().Quit();
        }
    }

    public override void _Process(double delta)
    {
        if (_world is null || _debugLabel is null || _villagersRoot is null)
            return;

        var builder = new StringBuilder();
        builder.AppendLine($"DominatusWorld ticks: {_world.TicksProcessed}");
        builder.AppendLine($"Agents: {_world.World.Agents.Count}");

        foreach (var child in _villagersRoot.GetChildren())
        {
            if (child is VillagerActor actor)
                builder.AppendLine(actor.BuildDebugSummary());
        }

        _debugLabel.Text = builder.ToString().TrimEnd();
    }
}
