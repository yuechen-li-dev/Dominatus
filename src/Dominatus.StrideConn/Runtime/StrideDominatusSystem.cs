using Stride.Core;
using Stride.Engine;
using Stride.Games;

namespace Dominatus.StrideConn;

public sealed class StrideDominatusSystem : GameSystem
{
    public StrideDominatusSystem(IServiceRegistry services)
        : this(services, new DominatusStrideRuntime())
    {
    }

    public StrideDominatusSystem(IServiceRegistry services, DominatusStrideRuntime runtime)
        : base(services)
    {
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

        Enabled = true;
        UpdateOrder = 0;

        services.AddService<IDominatusStrideRuntime>(Runtime);
    }

    public IDominatusStrideRuntime Runtime { get; }

    public override void Update(GameTime gameTime)
    {
        Runtime.World.Tick((float)gameTime.Elapsed.TotalSeconds);
    }
}
