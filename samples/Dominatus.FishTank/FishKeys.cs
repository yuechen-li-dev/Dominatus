using Dominatus.Core.Blackboard;

namespace Dominatus.Fishtank;

public static class FishKeys
{
    // Position and movement
    public static readonly BbKey<float> PosX = new("PosX");
    public static readonly BbKey<float> PosY = new("PosY");
    public static readonly BbKey<float> VelX = new("VelX");
    public static readonly BbKey<float> VelY = new("VelY");

    // Steering — desired velocity, integrated toward actual velocity each frame
    public static readonly BbKey<float> DesiredVelX = new("DesiredVelX");
    public static readonly BbKey<float> DesiredVelY = new("DesiredVelY");

    // Perception
    public static readonly BbKey<float> NearestFoodX = new("NearestFoodX");
    public static readonly BbKey<float> NearestFoodY = new("NearestFoodY");
    public static readonly BbKey<bool> FoodVisible = new("FoodVisible");
    public static readonly BbKey<float> NearestPredX = new("NearestPredX");
    public static readonly BbKey<float> NearestPredY = new("NearestPredY");
    public static readonly BbKey<bool> PredatorNearby = new("PredatorNearby");

    // Local motion shaping (Fishbowl-specific)
    public static readonly BbKey<float> SeparationX = new("SeparationX");
    public static readonly BbKey<float> SeparationY = new("SeparationY");
    public static readonly BbKey<float> FoodOffsetAngle = new("FoodOffsetAngle");

    // State
    public static readonly BbKey<float> Hunger = new("Hunger");
    public static readonly BbKey<float> WanderAngle = new("WanderAngle");
    public static readonly BbKey<bool> IsPredator = new("IsPredator");

    // Visual
    public static readonly BbKey<float> ColorR = new("ColorR");
    public static readonly BbKey<float> ColorG = new("ColorG");
    public static readonly BbKey<float> ColorB = new("ColorB");
    public static readonly BbKey<float> Radius = new("Radius");
}