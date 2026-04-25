using Stride.Engine;

namespace Dominatus.StrideConn.Tests;

public sealed class StrideEntityRegistryTests
{
    [Fact]
    public void StrideEntityRegistry_RegisterAndGetEntity()
    {
        var registry = new StrideEntityRegistry();
        var entity = new Entity();

        registry.Register("npc-1", entity);

        Assert.True(registry.TryGet("npc-1", out var actual));
        Assert.Same(entity, actual);
        Assert.Same(entity, registry.GetRequired("npc-1"));
    }

    [Fact]
    public void StrideEntityRegistry_RejectsEmptyId()
    {
        var registry = new StrideEntityRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register("", new Entity()));
        Assert.False(registry.TryGet("", out _));
    }

    [Fact]
    public void StrideEntityRegistry_RejectsNullEntity()
    {
        var registry = new StrideEntityRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register("npc-1", null!));
    }

    [Fact]
    public void StrideEntityRegistry_RejectsDuplicateId()
    {
        var registry = new StrideEntityRegistry();
        registry.Register("npc-1", new Entity());

        Assert.Throws<InvalidOperationException>(() => registry.Register("npc-1", new Entity()));
    }

    [Fact]
    public void StrideEntityRegistry_UnregisterRemovesEntity()
    {
        var registry = new StrideEntityRegistry();
        registry.Register("npc-1", new Entity());

        Assert.True(registry.Unregister("npc-1"));
        Assert.False(registry.TryGet("npc-1", out _));
    }

    [Fact]
    public void StrideEntityRegistry_GetRequiredMissingThrows()
    {
        var registry = new StrideEntityRegistry();

        Assert.Throws<KeyNotFoundException>(() => registry.GetRequired("missing"));
    }
}
