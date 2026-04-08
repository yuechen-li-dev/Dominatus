using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Dominatus.UtilityLite;

namespace Dominatus.Fishtank;

/// <summary>
/// Root node: utility decision between Flee, SeekFood, and Wander.
///
/// Key fix: Wander scores 0.4 (not 1.0 via Always), so Flee (1.0) and
/// SeekFood (1.0) always beat it cleanly. Previously Wander tied with
/// Flee/SeekFood at 1.0 and tie-breaking kept the current state (Wander),
/// meaning predator-nearby was never acted on.
/// </summary>
public static class FishNodes
{
    // -----------------------------------------------------------------------
    // Root — utility decision
    // -----------------------------------------------------------------------
    public static IEnumerator<AiStep> Root(AiCtx ctx)
    {
        var isPredator = ctx.Bb.GetOrDefault(FishKeys.IsPredator, false);

        if (isPredator)
        {
            while (true)
            {
                yield return Ai.Decide([
                    Utility.Option("Hunt",   Utility.Bb(FishKeys.FoodVisible), "Hunt"),
                    Utility.Option("Wander", Utility.Score((_, _) => 0.4f),   "Wander"),
                ], hysteresis: 0.05f, minCommitSeconds: 0.2f);
            }
        }
        else
        {
            while (true)
            {
                yield return Ai.Decide([
                    // Flee beats everything when a predator is close
                    Utility.Option("Flee",     Utility.Bb(FishKeys.PredatorNearby), "Flee"),
                    // SeekFood beats wander when food is visible
                    Utility.Option("SeekFood", Utility.Bb(FishKeys.FoodVisible),    "SeekFood"),
                    // Wander is the fallback — scores below 1.0 so it never ties
                    Utility.Option("Wander",   Utility.Score((_, _) => 0.4f),       "Wander"),
                ], hysteresis: 0.05f, minCommitSeconds: 0.1f); // fast react
            }
        }
    }

    // -----------------------------------------------------------------------
    // Wander — organic meander via Reynolds wander steering
    // -----------------------------------------------------------------------
    public static IEnumerator<AiStep> Wander(AiCtx ctx)
    {
        while (true)
        {
            yield return Ai.Act(new WanderCommand(Speed: 45f));
            yield return Ai.Wait(0.08f); // tighter loop = smoother curves
        }
    }

    // -----------------------------------------------------------------------
    // SeekFood — steer toward nearest food, re-read BB every iteration
    // -----------------------------------------------------------------------
    public static IEnumerator<AiStep> SeekFood(AiCtx ctx)
    {
        while (true)
        {
            var fx = ctx.Bb.GetOrDefault(FishKeys.NearestFoodX, 0f);
            var fy = ctx.Bb.GetOrDefault(FishKeys.NearestFoodY, 0f);
            yield return Ai.Act(new SteerTowardCommand(fx, fy, Speed: 65f));
            yield return Ai.Wait(0.05f);
        }
    }

    // -----------------------------------------------------------------------
    // Flee — steer away from predator, re-read BB every iteration
    // -----------------------------------------------------------------------
    public static IEnumerator<AiStep> Flee(AiCtx ctx)
    {
        while (true)
        {
            var px = ctx.Bb.GetOrDefault(FishKeys.NearestPredX, 0f);
            var py = ctx.Bb.GetOrDefault(FishKeys.NearestPredY, 0f);
            yield return Ai.Act(new SteerAwayCommand(px, py, Speed: 100f));
            yield return Ai.Wait(0.05f);
        }
    }

    // -----------------------------------------------------------------------
    // Hunt — predator chases nearest prey
    // -----------------------------------------------------------------------
    public static IEnumerator<AiStep> Hunt(AiCtx ctx)
    {
        while (true)
        {
            var tx = ctx.Bb.GetOrDefault(FishKeys.NearestFoodX, 0f);
            var ty = ctx.Bb.GetOrDefault(FishKeys.NearestFoodY, 0f);
            yield return Ai.Act(new SteerTowardCommand(tx, ty, Speed: 75f));
            yield return Ai.Wait(0.05f);
        }
    }
}