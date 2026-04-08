using Dominatus.Core.Hfsm;
using Dominatus.Core.Runtime;

namespace Dominatus.Fishtank;

public static class FishFactory
{
    private static readonly Random Rng = new();

    public static AiAgent CreatePrey(float x, float y, float r, float cr, float cg, float cb)
    {
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = FishNodes.Root });
        graph.Add(new HfsmStateDef { Id = "Wander", Node = FishNodes.Wander });
        graph.Add(new HfsmStateDef { Id = "SeekFood", Node = FishNodes.SeekFood });
        graph.Add(new HfsmStateDef { Id = "Flee", Node = FishNodes.Flee });

        var brain = new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true });
        var agent = new AiAgent(brain);

        var angle = Rng.NextSingle() * MathF.PI * 2f;
        agent.Bb.Set(FishKeys.PosX, x);
        agent.Bb.Set(FishKeys.PosY, y);
        agent.Bb.Set(FishKeys.VelX, MathF.Cos(angle) * 30f);
        agent.Bb.Set(FishKeys.VelY, MathF.Sin(angle) * 30f);
        agent.Bb.Set(FishKeys.DesiredVelX, MathF.Cos(angle) * 30f);
        agent.Bb.Set(FishKeys.DesiredVelY, MathF.Sin(angle) * 30f);
        agent.Bb.Set(FishKeys.WanderAngle, angle);
        agent.Bb.Set(FishKeys.Hunger, Rng.NextSingle());
        agent.Bb.Set(FishKeys.IsPredator, false);
        agent.Bb.Set(FishKeys.ColorR, cr);
        agent.Bb.Set(FishKeys.ColorG, cg);
        agent.Bb.Set(FishKeys.ColorB, cb);
        agent.Bb.Set(FishKeys.Radius, r);

        // Fishbowl-only shaping:
        // give each prey a tiny stable orbital preference around food so they
        // do not all converge on the exact same pixel.
        agent.Bb.Set(FishKeys.FoodOffsetAngle, Rng.NextSingle() * MathF.PI * 2f);
        agent.Bb.Set(FishKeys.SeparationX, 0f);
        agent.Bb.Set(FishKeys.SeparationY, 0f);

        return agent;
    }

    public static AiAgent CreatePredator(float x, float y)
    {
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = FishNodes.Root });
        graph.Add(new HfsmStateDef { Id = "Wander", Node = FishNodes.Wander });
        graph.Add(new HfsmStateDef { Id = "Hunt", Node = FishNodes.Hunt });

        var brain = new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true });
        var agent = new AiAgent(brain);

        var angle = Rng.NextSingle() * MathF.PI * 2f;
        agent.Bb.Set(FishKeys.PosX, x);
        agent.Bb.Set(FishKeys.PosY, y);
        agent.Bb.Set(FishKeys.VelX, MathF.Cos(angle) * 40f);
        agent.Bb.Set(FishKeys.VelY, MathF.Sin(angle) * 40f);
        agent.Bb.Set(FishKeys.DesiredVelX, MathF.Cos(angle) * 40f);
        agent.Bb.Set(FishKeys.DesiredVelY, MathF.Sin(angle) * 40f);
        agent.Bb.Set(FishKeys.WanderAngle, angle);
        agent.Bb.Set(FishKeys.IsPredator, true);
        agent.Bb.Set(FishKeys.ColorR, 0.9f);
        agent.Bb.Set(FishKeys.ColorG, 0.1f);
        agent.Bb.Set(FishKeys.ColorB, 0.1f);
        agent.Bb.Set(FishKeys.Radius, 14f);

        return agent;
    }
}