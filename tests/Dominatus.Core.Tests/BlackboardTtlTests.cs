using Dominatus.Core.Blackboard;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;
using Xunit;

namespace Dominatus.Core.Tests;

public sealed class BlackboardTtlTests
{
    private static readonly BbKey<string> Key = new("k");
    private static readonly BbKey<string> ResultKey = new("result");
    private static readonly BbKey<string> WorldKey = new("world.temp");

    [Fact]
    public void Blackboard_SetFor_StoresValueAndExpiry()
    {
        var bb = new Blackboard.Blackboard();

        bb.SetFor(Key, "v", now: 10f, ttlSeconds: 2f);

        Assert.Equal("v", bb.GetOrDefault(Key, ""));
        Assert.True(bb.TryGetExpiresAt(Key, out var exp));
        Assert.Equal(12f, exp);
    }

    [Fact]
    public void Blackboard_SetUntil_StoresValueAndExpiry()
    {
        var bb = new Blackboard.Blackboard();

        bb.SetUntil(Key, "v", 7f);

        Assert.True(bb.TryGetExpiresAt(Key, out var exp));
        Assert.Equal(7f, exp);
    }

    [Fact]
    public void Blackboard_SetFor_RejectsNaNOrInfinity()
    {
        var bb = new Blackboard.Blackboard();

        Assert.Throws<ArgumentOutOfRangeException>(() => bb.SetFor(Key, "v", float.NaN, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => bb.SetFor(Key, "v", 0f, float.PositiveInfinity));
    }

    [Fact]
    public void Blackboard_SetUntil_RejectsNaNOrInfinity()
    {
        var bb = new Blackboard.Blackboard();

        Assert.Throws<ArgumentOutOfRangeException>(() => bb.SetUntil(Key, "v", float.NegativeInfinity));
    }

    [Fact]
    public void Blackboard_Set_NormalSetClearsTtl()
    {
        var bb = new Blackboard.Blackboard();
        bb.SetUntil(Key, "v", 10f);

        bb.Set(Key, "changed");

        Assert.False(bb.TryGetExpiresAt(Key, out _));
    }

    [Fact]
    public void Blackboard_Set_SameValueWithExistingTtlClearsTtlAndDirties()
    {
        var bb = new Blackboard.Blackboard();
        bb.SetUntil(Key, "v", 10f);
        bb.ClearDirty();
        var before = bb.Revision;

        bb.Set(Key, "v");

        Assert.False(bb.TryGetExpiresAt(Key, out _));
        Assert.Contains(Key.Name, bb.DirtyKeys);
        Assert.Equal(before + 1, bb.Revision);
    }

    [Fact]
    public void Blackboard_SetUntil_SameValueDifferentExpiryRefreshesAndDirties()
    {
        var bb = new Blackboard.Blackboard();
        bb.SetUntil(Key, "v", 10f);
        bb.ClearDirty();
        var before = bb.Revision;

        bb.SetUntil(Key, "v", 12f);

        Assert.True(bb.TryGetExpiresAt(Key, out var exp));
        Assert.Equal(12f, exp);
        Assert.Contains(Key.Name, bb.DirtyKeys);
        Assert.Equal(before + 1, bb.Revision);
    }

    [Fact]
    public void Blackboard_ClearTtl_RemovesExpiryAndDirties()
    {
        var bb = new Blackboard.Blackboard();
        bb.SetUntil(Key, "v", 10f);
        bb.ClearDirty();
        var before = bb.Revision;

        var changed = bb.ClearTtl(Key);

        Assert.True(changed);
        Assert.False(bb.TryGetExpiresAt(Key, out _));
        Assert.Contains(Key.Name, bb.DirtyKeys);
        Assert.Equal(before + 1, bb.Revision);
    }

    [Fact]
    public void Blackboard_Expire_RemovesExpiredKey()
    {
        var bb = new Blackboard.Blackboard();
        bb.SetUntil(Key, "v", 1f);

        bb.Expire(1f);

        Assert.False(bb.TryGet(Key, out _));
        Assert.False(bb.TryGetExpiresAt(Key, out _));
    }

    [Fact]
    public void Blackboard_Expire_DoesNotRemoveUnexpiredKey()
    {
        var bb = new Blackboard.Blackboard();
        bb.SetUntil(Key, "v", 5f);

        bb.Expire(4.9f);

        Assert.True(bb.TryGet(Key, out _));
    }

    [Fact]
    public void Blackboard_Expire_MarksExpiredKeyDirtyAndBumpsRevision()
    {
        var bb = new Blackboard.Blackboard();
        bb.SetUntil(Key, "v", 5f);
        bb.ClearDirty();
        var before = bb.Revision;

        bb.Expire(5f);

        Assert.Contains(Key.Name, bb.DirtyKeys);
        Assert.Equal(before + 1, bb.Revision);
    }

    [Fact]
    public void Blackboard_Expire_ReturnsExpiredCount()
    {
        var bb = new Blackboard.Blackboard();
        bb.SetUntil(new BbKey<string>("a"), "1", 1f);
        bb.SetUntil(new BbKey<string>("b"), "2", 2f);
        bb.SetUntil(new BbKey<string>("c"), "3", 10f);

        var count = bb.Expire(2f);

        Assert.Equal(2, count);
    }

    [Fact]
    public void Blackboard_Remove_RemovesTtlMetadata()
    {
        var bb = new Blackboard.Blackboard();
        bb.SetUntil(Key, "v", 5f);

        bb.Remove(Key);

        Assert.False(bb.TryGetExpiresAt(Key, out _));
    }

    [Fact]
    public void Blackboard_Clear_RemovesTtlMetadata()
    {
        var bb = new Blackboard.Blackboard();
        bb.SetUntil(Key, "v", 5f);

        bb.Clear();

        Assert.False(bb.TryGetExpiresAt(Key, out _));
    }

    [Fact]
    public void Blackboard_TryGet_DoesNotExpireOnRead()
    {
        var bb = new Blackboard.Blackboard();
        bb.SetUntil(Key, "v", 1f);

        var found = bb.TryGet(Key, out var val);

        Assert.True(found);
        Assert.Equal("v", val);
        Assert.True(bb.TryGetExpiresAt(Key, out _));
    }

    [Fact]
    public void AiAgent_Tick_ExpiresAgentBlackboardBeforeBrainTick()
    {
        static IEnumerator<AiStep> Node(AiCtx ctx)
        {
            var visible = ctx.Bb.TryGet(Key, out _) ? "visible" : "expired";
            ctx.Bb.Set(ResultKey, visible);
            while (true) yield return null!;
        }

        var graph = new HfsmGraph { Root = "root" };
        graph.Add(new HfsmStateDef { Id = "root", Node = Node });
        var world = new AiWorld();
        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        agent.Bb.SetUntil(Key, "v", 0f);

        world.Tick(0.01f);

        Assert.Equal("expired", agent.Bb.GetOrDefault(ResultKey, "missing"));
    }

    [Fact]
    public void AiWorld_Tick_ExpiresWorldBlackboardBeforeAgentTick()
    {
        static IEnumerator<AiStep> Node(AiCtx ctx)
        {
            var visible = ctx.WorldBb.TryGet(WorldKey, out _) ? "visible" : "expired";
            ctx.Bb.Set(ResultKey, visible);
            while (true) yield return null!;
        }

        var graph = new HfsmGraph { Root = "root" };
        graph.Add(new HfsmStateDef { Id = "root", Node = Node });
        var world = new AiWorld();
        var agent = new AiAgent(new HfsmInstance(graph));
        world.Add(agent);
        world.Bb.SetUntil(WorldKey, "v", 0f);

        world.Tick(0.01f);

        Assert.Equal("expired", agent.Bb.GetOrDefault(ResultKey, "missing"));
    }
}
