using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.UtilityLite;
using Dominatus.OptFlow;

namespace Dominatus.SimConsole;

internal static class GuardScript
{
    public static readonly BbKey<bool> Alerted = new("Guard.Alerted");
    public static readonly BbKey<bool> UnderFire = new("Guard.UnderFire");
    public static readonly BbKey<bool> LowAmmo = new("Guard.LowAmmo");

    public static void Register(HfsmGraph graph)
    {
        graph.Add(new HfsmStateDef { Id = "Root", Node = Root });
        graph.Add(new HfsmStateDef { Id = "Patrol", Node = Patrol });
        graph.Add(new HfsmStateDef { Id = "Combat", Node = Combat });
        graph.Add(new HfsmStateDef { Id = "Reload", Node = Reload });
    }

    public static IEnumerator<AiStep> Root(AiCtx _)
    {
        while (true)
        {
            yield return Ai.Wait(0.10f);

            yield return Ai.Decide([
                Ai.Option("Combat", When.Combat, "Combat"),
                Ai.Option("Reload", When.Reload, "Reload"),
                Ai.Option("Patrol", When.Patrol, "Patrol"),
            ], hysteresis: 0.10f, minCommitSeconds: 0.75f);
        }
    }

    public static IEnumerator<AiStep> Patrol(AiCtx ctx)
    {
        var w = ctx.World;
        var a = ctx.Agent;

        while (true)
        {
            yield return Ai.Wait(0.25f);

            // Demo: flip alert at t≈2.
            if (w.Clock.Time >= 2.0f)
                a.Bb.Set(Alerted, true);
        }
    }

    public static IEnumerator<AiStep> Combat(AiCtx ctx)
    {
        var w = ctx.World;
        var a = ctx.Agent;

        while (true)
        {
            yield return Ai.Wait(0.25f);

            // Demo: low ammo at t≈3.5 (to show min-commit/hysteresis interplay).
            if (w.Clock.Time >= 3.5f)
                a.Bb.Set(LowAmmo, true);

            // Demo: calm down at t≈6.
            if (w.Clock.Time >= 6.0f)
                a.Bb.Set(Alerted, false);
        }
    }

    public static IEnumerator<AiStep> Reload(AiCtx ctx)
    {
        var a = ctx.Agent;

        // Simple one-shot action: wait, then clear low ammo and return.
        yield return Ai.Wait(1.0f);
        a.Bb.Set(LowAmmo, false);
        yield return Ai.Succeed("Reloaded");
    }

    private static class When
    {
        public static Consideration Alerted => Utility.Bool((w, a) => a.Bb.GetOrDefault(GuardScript.Alerted, false));
        public static Consideration LowAmmo => Utility.Bool((w, a) => a.Bb.GetOrDefault(GuardScript.LowAmmo, false));
        public static Consideration Combat => Utility.All(Alerted, Utility.Not(LowAmmo));
        public static Consideration Reload => LowAmmo;
        public static Consideration Patrol => Utility.Not(Alerted);
    }
}
