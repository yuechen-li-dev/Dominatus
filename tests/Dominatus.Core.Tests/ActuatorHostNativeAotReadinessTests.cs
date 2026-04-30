using Dominatus.Core.Runtime;
using Xunit;

namespace Dominatus.Core.Tests;

public sealed class ActuatorHostNativeAotReadinessTests
{
    private sealed record ImmediateStringCommand(string Value) : IActuationCommand;
    private sealed record DeferredStringCommand(string Value) : IActuationCommand;
    private sealed record DeferredUntypedCommand(string Value) : IActuationCommand;

    private sealed class ImmediateStringHandler : IActuationHandler<ImmediateStringCommand>
    {
        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, ImmediateStringCommand cmd)
            => ActuatorHost.HandlerResult.CompletedWithPayload(cmd.Value);
    }

    private sealed class DeferredStringHandler : IActuationHandler<DeferredStringCommand>
    {
        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DeferredStringCommand cmd)
        {
            host.CompleteLater(ctx, id, ctx.World.Clock.Time + 1f, ok: true, payload: cmd.Value);
            return ActuatorHost.HandlerResult.DeferredAccepted();
        }
    }

    private sealed class DeferredUntypedHandler : IActuationHandler<DeferredUntypedCommand>
    {
        public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, DeferredUntypedCommand cmd)
        {
            host.CompleteLater(ctx, id, ctx.World.Clock.Time + 1f, ok: true, payload: cmd.Value, payloadType: typeof(string));
            return ActuatorHost.HandlerResult.DeferredAccepted();
        }
    }

    [Fact]
    public void ActuatorHost_ImmediateTypedPayload_PublishesTypedCompletion()
    {
        var host = new ActuatorHost();
        host.Register(new ImmediateStringHandler());
        var (world, agent, ctx) = CreateWorld(host);

        var dispatch = host.Dispatch(ctx, new ImmediateStringCommand("hello"));

        Assert.True(dispatch.Completed);
        EventCursor cursor = default;
        Assert.True(agent.Events.TryConsume(ref cursor, (ActuationCompleted<string> e) => e.Id.Equals(dispatch.Id), out var typed));
        Assert.Equal("hello", typed.Payload);
    }

    [Fact]
    public void ActuatorHost_DeferredTypedPayload_PublishesTypedCompletion()
    {
        var host = new ActuatorHost();
        host.Register(new DeferredStringHandler());
        var (world, agent, ctx) = CreateWorld(host);

        var dispatch = host.Dispatch(ctx, new DeferredStringCommand("later"));
        world.Tick(2f);

        EventCursor cursor = default;
        Assert.True(agent.Events.TryConsume(ref cursor, (ActuationCompleted<string> e) => e.Id.Equals(dispatch.Id), out var typed));
        Assert.Equal("later", typed.Payload);
    }

    [Fact]
    public void ActuatorHost_DeferredTypedPayload_RecordsPayloadTypeTag()
    {
        var host = new ActuatorHost();
        host.Register(new DeferredStringHandler());
        var (_, agent, ctx) = CreateWorld(host);

        host.Dispatch(ctx, new DeferredStringCommand("x"));

        Assert.Single(agent.InFlightActuations);
        Assert.Equal("string", agent.InFlightActuations.Single().PayloadTypeTag);
    }

    [Fact]
    public void ActuatorHost_NonGenericCompleteLater_DoesNotPublishTypedCompletion()
    {
        var host = new ActuatorHost();
        host.Register(new DeferredUntypedHandler());
        var (world, agent, ctx) = CreateWorld(host);

        var dispatch = host.Dispatch(ctx, new DeferredUntypedCommand("later-untyped"));
        world.Tick(2f);

        EventCursor untypedCursor = default;
        Assert.True(agent.Events.TryConsume(ref untypedCursor, (ActuationCompleted e) => e.Id.Equals(dispatch.Id), out _));

        EventCursor typedCursor = default;
        Assert.False(agent.Events.TryConsume(ref typedCursor, (ActuationCompleted<string> e) => e.Id.Equals(dispatch.Id), out _));
    }

    [Fact]
    public void AiEventBus_DoesNotExposePublishObject()
    {
        Assert.Null(typeof(AiEventBus).GetMethod("PublishObject"));
    }

    [Fact]
    public void ActuatorHost_Source_HasNoReflectionTypedCompletionPath()
    {
        var source = File.ReadAllText(FindFromRepoRoot("src/Dominatus.Core/Runtime/ActuatorHost.cs"));
        Assert.DoesNotContain("MakeGenericType", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator.CreateInstance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishObject", source, StringComparison.Ordinal);
    }

    private static (AiWorld world, AiAgent agent, AiCtx ctx) CreateWorld(ActuatorHost host)
    {
        var world = new AiWorld(host);
        var agent = new AiAgent(TestGraphs.MakeBareBrain());
        world.Add(agent);
        var ctx = new AiCtx(world, agent, agent.Events, default, world.View, world.Mail, host);
        return (world, agent, ctx);
    }

    private static string FindFromRepoRoot(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException($"Could not locate '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
