using Dominatus.Core.Runtime;

namespace Dominatus.Core.Tests;

public sealed class AsyncActuationTests
{
    private sealed class OwnedReader : AsyncActuationHandler<ReadCommand>, IDisposable
    {
        public int Disposals;
        public bool Finished;
        public override async ValueTask<ActuatorHost.HandlerResult> HandleAsync(AsyncActuationContext context, ReadCommand command, CancellationToken ct)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return ActuatorHost.HandlerResult.CompletedOk(); }
            finally { Finished = true; }
        }
        public void Dispose() { Assert.True(Finished); Disposals++; }
    }

    [Fact]
    public async Task Owned_resources_are_disposed_once_after_work_finishes_and_borrowed_handlers_stay_borrowed()
    {
        await using var host = new ActuatorHost(); var owned = new OwnedReader();
        host.Register(owned, ownsHandler: true); host.Register(owned, ownsHandler: true);
        var (_, _, ctx) = Context(host); host.Dispatch(ctx, new ReadCommand());
        await host.DisposeAsync(); Assert.Equal(1, owned.Disposals);
        await using var borrower = new ActuatorHost(); var borrowed = new OwnedReader();
        borrower.Register(borrowed); await borrower.DisposeAsync(); Assert.Equal(0, borrowed.Disposals);
    }
    private sealed record Progress(int Index);
    private sealed class StreamingReader : AsyncActuationHandler<ReadCommand>
    {
        public AsyncActuationContext Invocation;
        public readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Reported = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<ActuatorHost.HandlerResult> HandleAsync(AsyncActuationContext context, ReadCommand command, CancellationToken ct)
        {
            Invocation = context;
            await Release.Task.WaitAsync(ct);
            context.Report(new Progress(1)); context.Report(new Progress(2)); Reported.SetResult(true);
            return ActuatorHost.HandlerResult.CompletedWithPayload("done");
        }
    }

    [Fact]
    public async Task Progress_is_queued_until_tick_in_order_and_closed_after_completion()
    {
        await using var host = new ActuatorHost(); var reader = new StreamingReader(); host.Register(reader);
        var (world, agent, ctx) = Context(host); EventCursor progress = default, completion = default;
        Assert.False(host.Dispatch(ctx, new ReadCommand()).Completed);
        reader.Release.SetResult(true); await reader.Reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(agent.Events.TryConsume<Progress>(ref progress, null, out _));
        await Drain(host, world, agent);
        Assert.True(agent.Events.TryConsume<Progress>(ref progress, null, out var first)); Assert.Equal(1, first.Index);
        Assert.True(agent.Events.TryConsume<Progress>(ref progress, null, out var last)); Assert.Equal(2, last.Index);
        Assert.True(agent.Events.TryConsume<ActuationCompleted>(ref completion, null, out var result)); Assert.True(result.Ok);
        reader.Invocation.Report(new Progress(3)); host.Tick(world);
        Assert.False(agent.Events.TryConsume<Progress>(ref progress, null, out _));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("remove")]
    [InlineData("dispose")]
    public async Task Queued_progress_is_suppressed_when_its_owner_is_canceled_removed_or_disposed(string action)
    {
        await using var host = new ActuatorHost(); var reader = new StreamingReader(); host.Register(reader);
        using var cancel = new CancellationTokenSource();
        var (world, agent, ctx) = Context(host, cancel.Token); EventCursor cursor = default;
        host.Dispatch(ctx, new ReadCommand()); reader.Release.SetResult(true);
        await reader.Reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (action == "cancel") cancel.Cancel();
        if (action == "remove") world.Remove(agent);
        if (action == "dispose") await host.DisposeAsync();
        else await Drain(host, world, agent);
        Assert.False(agent.Events.TryConsume<Progress>(ref cursor, null, out _));
    }
    private sealed record ReadCommand : IActuationCommand;
    private sealed class Reader : AsyncActuationHandler<ReadCommand>
    {
        public readonly TaskCompletionSource<string> Source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token;
        public override async ValueTask<ActuatorHost.HandlerResult> HandleAsync(AsyncActuationContext context, ReadCommand command, CancellationToken ct)
        { Token = ct; return ActuatorHost.HandlerResult.CompletedWithPayload(await Source.Task.WaitAsync(ct)); }
    }
    private static (AiWorld World, AiAgent Agent, AiCtx Context) Context(ActuatorHost host, CancellationToken ct = default)
    {
        var world = new AiWorld(host); var agent = new AiAgent(TestGraphs.MakeBareBrain()); world.Add(agent);
        return (world, agent, new(world, agent, agent.Events, ct, world.View, world.Mail, host));
    }
    private static async Task Drain(ActuatorHost host, AiWorld world, AiAgent agent)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (agent.InFlightActuations.Count > 0) { timeout.Token.ThrowIfCancellationRequested(); host.Tick(world); await Task.Yield(); }
    }
    [Fact]
    public async Task Dispatch_does_not_block_and_completion_publishes_only_on_owner_tick_once()
    {
        await using var host = new ActuatorHost(); var reader = new Reader(); host.Register(reader);
        EventCursor cursor = default, typedCursor = default; var (world, agent, ctx) = Context(host); var result = host.Dispatch(ctx, new ReadCommand());
        Assert.False(result.Completed); Assert.Single(agent.InFlightActuations);
        reader.Source.SetResult("evidence");
        Assert.False(agent.Events.TryConsume<ActuationCompleted>(ref cursor, null, out _));
        await Drain(host, world, agent);
        Assert.True(agent.Events.TryConsume<ActuationCompleted>(ref cursor, null, out var completed));
        Assert.Equal("evidence", completed.Payload); Assert.Equal(result.Id, completed.Id);
        Assert.True(agent.Events.TryConsume<ActuationCompleted<string>>(ref typedCursor, null, out var typed)); Assert.Equal("evidence", typed.Payload);
        host.Tick(world); Assert.False(agent.Events.TryConsume<ActuationCompleted>(ref cursor, null, out _));
    }
    [Fact]
    public async Task Cancellation_and_failure_are_terminal_and_never_publish_late_success()
    {
        await using var host = new ActuatorHost(); var reader = new Reader(); host.Register(reader);
        using var cancellation = new CancellationTokenSource(); EventCursor cursor = default; var (world, agent, ctx) = Context(host, cancellation.Token);
        host.Dispatch(ctx, new ReadCommand()); cancellation.Cancel(); await Drain(host, world, agent);
        Assert.True(agent.Events.TryConsume<ActuationCompleted>(ref cursor, null, out var completed)); Assert.False(completed.Ok);
        reader.Source.SetResult("late"); host.Tick(world); Assert.False(agent.Events.TryConsume<ActuationCompleted>(ref cursor, null, out _));
    }
    [Fact]
    public async Task Removed_agents_and_disposed_hosts_cancel_owned_work_without_world_callbacks()
    {
        var host = new ActuatorHost(); var reader = new Reader(); host.Register(reader);
        EventCursor cursor = default; var (world, agent, ctx) = Context(host); host.Dispatch(ctx, new ReadCommand());
        world.Remove(agent); host.Tick(world); Assert.True(reader.Token.IsCancellationRequested);
        await host.DisposeAsync(); Assert.Empty(agent.InFlightActuations);
        Assert.False(agent.Events.TryConsume<ActuationCompleted>(ref cursor, null, out _));
        Assert.Throws<ObjectDisposedException>(() => host.Dispatch(ctx, new ReadCommand()));
    }
    [Fact]
    public async Task Policy_denial_prevents_start_and_faults_are_observed_without_payload_leakage()
    {
        await using var host = new ActuatorHost(); var reader = new Reader(); host.Register(reader);
        EventCursor cursor = default; var (world, agent, ctx) = Context(host);
        reader.Source.SetException(new Exception("private credentials"));
        host.Dispatch(ctx, new ReadCommand()); await Drain(host, world, agent);
        Assert.True(agent.Events.TryConsume<ActuationCompleted>(ref cursor, null, out var failure)); Assert.Equal("Async actuation failed.", failure.Error);
        host.AddPolicy(ActuationPolicies.DenyAll("closed"));
        Assert.False(host.Dispatch(ctx, new ReadCommand()).Accepted);
    }
}
