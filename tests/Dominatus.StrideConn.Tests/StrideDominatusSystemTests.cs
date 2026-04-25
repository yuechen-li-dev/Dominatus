using Stride.Core;
using Stride.Games;

namespace Dominatus.StrideConn.Tests;

public sealed class StrideDominatusSystemTests
{
    [Fact]
    public void StrideDominatusSystem_RegistersRuntimeService()
    {
        var services = new ServiceRegistry();

        var system = new StrideDominatusSystem(services);

        var runtime = services.GetService<IDominatusStrideRuntime>();
        Assert.NotNull(runtime);
        Assert.Same(runtime, system.Runtime);
    }

    [Fact]
    public void StrideDominatusSystem_UpdateTicksWorldClock()
    {
        var services = new ServiceRegistry();
        var system = new StrideDominatusSystem(services);

        var before = system.Runtime.World.Clock.Time;
        var gameTime = new GameTime(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(0.25));

        system.Update(gameTime);

        Assert.Equal(before + 0.25f, system.Runtime.World.Clock.Time, 3);
    }
}
