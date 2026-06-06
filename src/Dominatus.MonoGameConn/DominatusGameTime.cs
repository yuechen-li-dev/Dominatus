using Microsoft.Xna.Framework;

namespace Dominatus.MonoGameConn;

public static class DominatusGameTime
{
    public static float ToDeltaSeconds(GameTime gameTime, float timeScale = 1f)
    {
        if (gameTime is null) throw new ArgumentNullException(nameof(gameTime));
        ValidateTimeScale(timeScale);

        return (float)(gameTime.ElapsedGameTime.TotalSeconds * timeScale);
    }

    internal static void ValidateTimeScale(float value)
    {
        if (!float.IsFinite(value) || value < 0f)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Time scale must be finite and greater than or equal to zero.");
    }
}
