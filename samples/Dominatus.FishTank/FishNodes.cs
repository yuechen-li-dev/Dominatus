using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;
using Dominatus.UtilityLite;

namespace Dominatus.Fishtank;

/// <summary>
/// Root node: utility decision between Flee, SeekFood, and Wander.
/// Runs forever, re-evaluating every tick via Ai.Decide.
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
            // Predators: hunt prey or wander
            while (true)
            {
                yield return Ai.Decide([
                    Utility.Option("Hunt",   Utility.Bb(FishKeys.FoodVisible),  "Hunt"),
                    Utility.Option("Wander", Utility.Always,                    "Wander"),
                ], hysteresis: 0.05f, minCommitSeconds: 0.5f);
            }
        }
        else
        {
            // Prey: flee > seek food > wander, in that priority
            while (true)
            {
                yield return Ai.Decide([
                    Utility.Option("Flee",     Utility.Bb(FishKeys.PredatorNearby), "Flee"),
                    Utility.Option("SeekFood", Utility.Bb(FishKeys.FoodVisible),    "SeekFood"),
                    Utility.Option("Wander",   Utility.Always,                      "Wander"),
                ], hysteresis: 0.05f, minCommitSeconds: 0.3f);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Wander — just meander around
    // -----------------------------------------------------------------------
    public static IEnumerator<AiStep> Wander(AiCtx ctx)
    {
        while (true)
        {
            yield return Ai.Act(new WanderCommand(Speed: 40f));
            yield return Ai.Wait(0.15f);
        }
    }

    // -----------------------------------------------------------------------
    // SeekFood — steer toward nearest food
    // -----------------------------------------------------------------------
    public static IEnumerator<AiStep> SeekFood(AiCtx ctx)
    {
        while (true)
        {
            var fx = ctx.Bb.GetOrDefault(FishKeys.NearestFoodX, 0f);
            var fy = ctx.Bb.GetOrDefault(FishKeys.NearestFoodY, 0f);
            yield return Ai.Act(new SteerTowardCommand(fx, fy, Speed: 60f));
            yield return Ai.Wait(0.05f);
        }
    }

    // -----------------------------------------------------------------------
    // Flee — steer away from nearest predator
    // -----------------------------------------------------------------------
    public static IEnumerator<AiStep> Flee(AiCtx ctx)
    {
        while (true)
        {
            var px = ctx.Bb.GetOrDefault(FishKeys.NearestPredX, 0f);
            var py = ctx.Bb.GetOrDefault(FishKeys.NearestPredY, 0f);
            yield return Ai.Act(new SteerAwayCommand(px, py, Speed: 90f));
            yield return Ai.Wait(0.05f);
        }
    }

    // -----------------------------------------------------------------------
    // Hunt — predator chases nearest prey (uses FoodVisible/NearestFood keys
    //        which FishtankGame repurposes for predator targets)
    // -----------------------------------------------------------------------
    public static IEnumerator<AiStep> Hunt(AiCtx ctx)
    {
        while (true)
        {
            var tx = ctx.Bb.GetOrDefault(FishKeys.NearestFoodX, 0f);
            var ty = ctx.Bb.GetOrDefault(FishKeys.NearestFoodY, 0f);
            yield return Ai.Act(new SteerTowardCommand(tx, ty, Speed: 70f));
            yield return Ai.Wait(0.05f);
        }
    }
}
