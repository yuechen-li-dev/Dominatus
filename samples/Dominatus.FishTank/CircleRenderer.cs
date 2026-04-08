using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Dominatus.Fishtank;

/// <summary>
/// Draws filled circles via a small pre-generated circle texture scaled at draw time.
/// No content pipeline required.
/// </summary>
public sealed class CircleRenderer : IDisposable
{
    private readonly Texture2D _tex;
    private const int TexSize = 64;

    public CircleRenderer(GraphicsDevice gd)
    {
        _tex = new Texture2D(gd, TexSize, TexSize);
        var data = new Color[TexSize * TexSize];
        var center = TexSize / 2f;
        var r = center - 1f;

        for (int y = 0; y < TexSize; y++)
        for (int x = 0; x < TexSize; x++)
        {
            var dx = x - center;
            var dy = y - center;
            var dist = MathF.Sqrt(dx * dx + dy * dy);
            // Soft edge for antialiasing feel
            var alpha = Math.Clamp(1f - (dist - r + 1f), 0f, 1f);
            data[y * TexSize + x] = new Color(1f, 1f, 1f, alpha);
        }

        _tex.SetData(data);
    }

    public void Draw(SpriteBatch sb, float cx, float cy, float radius, Color color)
    {
        var diameter = radius * 2f;
        var scale = diameter / TexSize;
        sb.Draw(
            _tex,
            new Vector2(cx, cy),
            null,
            color,
            0f,
            new Vector2(TexSize / 2f, TexSize / 2f),
            scale,
            SpriteEffects.None,
            0f);
    }

    public void Dispose() => _tex.Dispose();
}
