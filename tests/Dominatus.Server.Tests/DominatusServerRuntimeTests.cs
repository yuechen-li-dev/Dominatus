using Dominatus.Core.Runtime;
using Dominatus.Server;

namespace Dominatus.Server.Tests;

public class DominatusServerRuntimeTests
{
    [Fact]
    public void DominatusServerRuntime_ReadReturnsValueUnderGate()
    {
        var world = new AiWorld();
        var runtime = new DominatusServerRuntime(world);

        var time = runtime.Read(static w => w.Clock.Time);

        Assert.Equal(0f, time);
    }

    [Fact]
    public void DominatusServerRuntime_WriteMutatesWorldUnderGate()
    {
        var world = new AiWorld();
        var runtime = new DominatusServerRuntime(world);

        runtime.Write(static w => w.Tick(0.5f));

        var time = runtime.Read(static w => w.Clock.Time);
        Assert.Equal(0.5f, time);
    }

    [Fact]
    public void DominatusServerRuntime_RejectsNullWorld()
    {
        Assert.Throws<ArgumentNullException>(() => new DominatusServerRuntime(null!));
    }
}
