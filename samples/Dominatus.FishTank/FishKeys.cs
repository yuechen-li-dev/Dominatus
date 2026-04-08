using Dominatus.Core.Blackboard;
using Microsoft.Xna.Framework;

namespace Dominatus.Fishtank;

public static class FishKeys
{
    // Position and movement
    public static readonly BbKey<float> PosX        = new("PosX");
    public static readonly BbKey<float> PosY        = new("PosY");
    public static readonly BbKey<float> VelX        = new("VelX");
    public static readonly BbKey<float> VelY        = new("VelY");

    // Perception
    public static readonly BbKey<float> NearestFoodX    = new("NearestFoodX");
    public static readonly BbKey<float> NearestFoodY    = new("NearestFoodY");
    public static readonly BbKey<bool>  FoodVisible      = new("FoodVisible");
    public static readonly BbKey<float> NearestPredX    = new("NearestPredX");
    public static readonly BbKey<float> NearestPredY    = new("NearestPredY");
    public static readonly BbKey<bool>  PredatorNearby   = new("PredatorNearby");

    // State
    public static readonly BbKey<float> Hunger       = new("Hunger");
    public static readonly BbKey<float> WanderAngle  = new("WanderAngle");
    public static readonly BbKey<bool>  IsPredator   = new("IsPredator");

    // Visual
    public static readonly BbKey<float> ColorR = new("ColorR");
    public static readonly BbKey<float> ColorG = new("ColorG");
    public static readonly BbKey<float> ColorB = new("ColorB");
    public static readonly BbKey<float> Radius  = new("Radius");
}
