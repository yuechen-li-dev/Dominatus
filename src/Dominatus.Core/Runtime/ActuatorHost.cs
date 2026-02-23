using System.Runtime.CompilerServices;

namespace Dominatus.Core.Runtime;

/// <summary>
/// Generic command dispatcher ("tools/skills host").
/// Commands can complete immediately or later via deferred completion events.
/// </summary>
public sealed class ActuatorHost : IAiActuator, ITickableActuator
{
    private long _nextId = 1;

    private readonly Dictionary<Type, IHandler> _handlers = new();

    // Deferred completions: emitted later as ActuationCompleted into the target agent's event bus.
    private readonly List<PendingCompletion> _pending = new();

    private readonly struct PendingCompletion
    {
        public readonly AgentId AgentId;
        public readonly ActuationId Id;
        public readonly float DueTime;
        public readonly bool Ok;
        public readonly string? Error;
        public readonly object? Payload;

        public PendingCompletion(AgentId agentId, ActuationId id, float dueTime, bool ok, string? error, object? payload)
        {
            AgentId = agentId;
            Id = id;
            DueTime = dueTime;
            Ok = ok;
            Error = error;
            Payload = payload;
        }
    }

    public void Register<TCmd>(IActuationHandler<TCmd> handler) where TCmd : notnull, IActuationCommand
        => _handlers[typeof(TCmd)] = new HandlerAdapter<TCmd>(handler);

    public ActuationDispatchResult Dispatch(AiCtx ctx, IActuationCommand command)
    {
        var id = new ActuationId(_nextId++);

        if (!_handlers.TryGetValue(command.GetType(), out var h))
        {
            var miss = new ActuationDispatchResult(id, Accepted: false, Completed: true, Ok: false, Error: "No handler registered.");
            // Normalize: publish completion event for uniform Await behavior
            ctx.Agent.Events.Publish(new ActuationCompleted(miss.Id, miss.Ok, miss.Error, miss.Payload));
            return miss;
        }

        var r = h.Handle(this, ctx, id, command);

        var res = new ActuationDispatchResult(
            Id: id,
            Accepted: r.Accepted,
            Completed: r.Completed,
            Ok: r.Ok,
            Error: r.Error,
            Payload: r.Payload);

        // Normalize: immediate completion publishes an ActuationCompleted event.
        if (res.Completed)
            ctx.Agent.Events.Publish(new ActuationCompleted(res.Id, res.Ok, res.Error, res.Payload));

        return res;
    }

    /// <summary>
    /// Schedule a deferred completion. This is the "async-ish await" mechanism:
    /// complete later by publishing ActuationCompleted at/after dueTime.
    /// </summary>
    public void CompleteLater(AiCtx ctx, ActuationId id, float dueTime, bool ok, string? error = null, object? payload = null)
    {
        _pending.Add(new PendingCompletion(ctx.Agent.Id, id, dueTime, ok, error, payload));
    }

    public void Tick(AiWorld world)
    {
        if (_pending.Count == 0) return;

        float now = world.Clock.Time;

        // In-place compacting to avoid allocations
        int write = 0;
        for (int read = 0; read < _pending.Count; read++)
        {
            var p = _pending[read];
            if (p.DueTime <= now)
            {
                var agent = FindAgent(world, p.AgentId);
                if (agent is not null)
                    agent.Events.Publish(new ActuationCompleted(p.Id, p.Ok, p.Error, p.Payload));
                continue;
            }

            _pending[write++] = p;
        }

        if (write < _pending.Count)
            _pending.RemoveRange(write, _pending.Count - write);
    }

    private static AiAgent? FindAgent(AiWorld world, AgentId id)
    {
        var agents = world.Agents;
        for (int i = 0; i < agents.Count; i++)
            if (agents[i].Id.Equals(id))
                return agents[i];

        return null;
    }

    // ----------------- handler plumbing -----------------

    public interface IHandler
    {
        HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, IActuationCommand cmd);
    }

    public readonly record struct HandlerResult(bool Accepted, bool Completed, bool Ok, string? Error = null, object? Payload = null);

    private sealed class HandlerAdapter<TCmd> : IHandler where TCmd : notnull, IActuationCommand
    {
        private readonly IActuationHandler<TCmd> _inner;
        public HandlerAdapter(IActuationHandler<TCmd> inner) => _inner = inner;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, IActuationCommand cmd)
            => _inner.Handle(host, ctx, id, (TCmd)cmd);
    }
}