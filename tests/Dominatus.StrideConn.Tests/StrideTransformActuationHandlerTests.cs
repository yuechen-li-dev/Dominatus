using Dominatus.Core.Runtime;
using Stride.Core.Mathematics;
using Stride.Engine;

namespace Dominatus.StrideConn.Tests;

public sealed class StrideTransformActuationHandlerTests
{
    [Fact]
    public void StrideTransformHandler_SetEntityPosition_UpdatesTransform()
    {
        var registry = new StrideEntityRegistry();
        var entity = new Entity();
        registry.Register("npc-1", entity);

        var handler = new StrideTransformActuationHandler(registry);
        var result = handler.Handle(new ActuatorHost(), default, 1, new SetEntityPositionCommand("npc-1", new Vector3(1, 2, 3)));

        Assert.True(result.Accepted);
        Assert.True(result.Completed);
        Assert.True(result.Ok);
        Assert.Equal(new Vector3(1, 2, 3), entity.Transform.Position);
    }

    [Fact]
    public void StrideTransformHandler_MoveEntityBy_AddsDelta()
    {
        var registry = new StrideEntityRegistry();
        var entity = new Entity();
        entity.Transform.Position = new Vector3(5, 0, 0);
        registry.Register("npc-1", entity);

        var handler = new StrideTransformActuationHandler(registry);
        var result = handler.Handle(new ActuatorHost(), default, 1, new MoveEntityByCommand("npc-1", new Vector3(1, 2, 3)));

        Assert.True(result.Accepted);
        Assert.True(result.Ok);
        Assert.Equal(new Vector3(6, 2, 3), entity.Transform.Position);
    }

    [Fact]
    public void StrideTransformHandler_MissingEntityFails()
    {
        var handler = new StrideTransformActuationHandler(new StrideEntityRegistry());

        var result = handler.Handle(new ActuatorHost(), default, 1, new MoveEntityByCommand("missing", new Vector3(1, 0, 0)));

        Assert.False(result.Accepted);
        Assert.True(result.Completed);
        Assert.False(result.Ok);
        Assert.Contains("missing", result.Error);
    }
}
