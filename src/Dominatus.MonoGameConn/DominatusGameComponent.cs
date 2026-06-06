using Dominatus.Core.Runtime;
using Microsoft.Xna.Framework;

namespace Dominatus.MonoGameConn;

public sealed class DominatusGameComponent : GameComponent
{
    private float _timeScale = 1f;

    public DominatusGameComponent(Game game, AiWorld world)
        : base(game ?? throw new ArgumentNullException(nameof(game)))
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
    }

    public AiWorld World { get; }

    public bool IsPaused { get; set; }

    public float TimeScale
    {
        get => _timeScale;
        set
        {
            DominatusGameTime.ValidateTimeScale(value);
            _timeScale = value;
        }
    }

    public long UpdatesProcessed { get; private set; }

    public float LastDeltaSeconds { get; private set; }

    public override void Update(GameTime gameTime)
    {
        if (gameTime is null) throw new ArgumentNullException(nameof(gameTime));

        if (IsPaused)
            return;

        var dt = DominatusGameTime.ToDeltaSeconds(gameTime, TimeScale);
        World.Tick(dt);
        LastDeltaSeconds = dt;
        UpdatesProcessed++;
    }
}
