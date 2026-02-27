using Dominatus.Core.Persistence;
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
    private readonly List<IActuationPolicy> _policies = new();

    // Deferred completions: emitted later as ActuationCompleted into the target agent's event bus.
    private readonly List<PendingCompletion> _pending = new();

    private readonly struct PendingCompletion(AgentId agentId, ActuationId id, float dueTime, bool ok, string? error, object? payload, Type? payloadType)
    {
        public readonly AgentId AgentId = agentId;
        public readonly ActuationId Id = id;
        public readonly float DueTime = dueTime;
        public readonly bool Ok = ok;
        public readonly string? Error = error;
        public readonly object? Payload = payload;
        public readonly Type? PayloadType = payloadType;
    }

    public void Register<TCmd>(IActuationHandler<TCmd> handler) where TCmd : notnull, IActuationCommand
        => _handlers[typeof(TCmd)] = new HandlerAdapter<TCmd>(handler);

    public void AddPolicy(IActuationPolicy policy)
    {
        if (policy is null) throw new ArgumentNullException(nameof(policy));
        _policies.Add(policy);
    }

    public ActuationDispatchResult Dispatch(AiCtx ctx, IActuationCommand command)
    {
        var id = new ActuationId(_nextId++);

        for (int i = 0; i < _policies.Count; i++)
        {
            var d = _policies[i].Evaluate(ctx, command);
            if (!d.Allowed)
            {
                var denied = new ActuationDispatchResult(
                    id,
                    Accepted: false,
                    Completed: true,
                    Ok: false,
                    Error: d.Reason ?? "Blocked by actuation policy.");

                ctx.Agent.Events.Publish(new ActuationCompleted(denied.Id, denied.Ok, denied.Error, denied.Payload));
                return denied;
            }
        }

        if (!_handlers.TryGetValue(command.GetType(), out var h))
        {
            var miss = new ActuationDispatchResult(id, Accepted: false, Completed: true, Ok: false, Error: "No handler registered.");
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

        if (res.Completed)
        {
            // Immediate completion — publish and ensure not lingering in in-flight set.
            ctx.Agent.Events.Publish(new ActuationCompleted(res.Id, res.Ok, res.Error, res.Payload));

            if (r.PayloadType is not null)
            {
                var completedT = typeof(ActuationCompleted<>).MakeGenericType(r.PayloadType);
                // TODO: Typed actuator completion uses reflection. Not hot path, but worth replacing.
                var typedEvt = Activator.CreateInstance(
                    completedT,
                    new object?[] { res.Id, res.Ok, res.Error, res.Payload });
                ctx.Agent.Events.PublishObject(typedEvt!);
            }
        }
        else if (res.Accepted)
        {
            // Deferred completion — register as in-flight so checkpoint capture can record it.
            // PayloadTypeTag mirrors BbJsonCodec's type table: "string" for string payload,
            // null for untyped (e.g. DiagLine which has no payload).
            var tag = PayloadTypeToTag(r.PayloadType);
            ctx.Agent.InFlightActuations.Add(new PendingActuation(id.Value, tag));
        }

        return res;
    }

    /// <summary>
    /// Schedule a deferred completion. This is the async-ish await mechanism:
    /// complete later by publishing ActuationCompleted at/after dueTime.
    /// </summary>
    public void CompleteLater(AiCtx ctx, ActuationId id, float dueTime, bool ok, string? error = null, object? payload = null, Type? payloadType = default)
    {
        _pending.Add(new PendingCompletion(ctx.Agent.Id, id, dueTime, ok, error, payload, payloadType));
    }

    public void CompleteLater<T>(AiCtx ctx, ActuationId id, float dueTime, bool ok, string? error = null, T? payload = default)
    {
        _pending.Add(new PendingCompletion(
            ctx.Agent.Id,
            id,
            dueTime,
            ok,
            error,
            payload,
            typeof(T)));
    }

    public void Tick(AiWorld world)
    {
        if (_pending.Count == 0) return;

        float now = world.Clock.Time;

        int write = 0;
        for (int read = 0; read < _pending.Count; read++)
        {
            var p = _pending[read];
            if (p.DueTime <= now)
            {
                var agent = FindAgent(world, p.AgentId);
                if (agent is not null)
                {
                    agent.Events.Publish(new ActuationCompleted(p.Id, p.Ok, p.Error, p.Payload));

                    if (p.PayloadType is not null)
                    {
                        var completedT = typeof(ActuationCompleted<>).MakeGenericType(p.PayloadType);
                        var typedEvt = Activator.CreateInstance(
                            completedT,
                            new object?[] { p.Id, p.Ok, p.Error, p.Payload });
                        agent.Events.PublishObject(typedEvt!);
                    }

                    // Deferred completion fired — remove from in-flight set.
                    agent.InFlightActuations.Remove(new PendingActuation(p.Id.Value, null));
                }
                continue;
            }

            _pending[write++] = p;
        }

        if (write < _pending.Count)
            _pending.RemoveRange(write, _pending.Count - write);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static AiAgent? FindAgent(AiWorld world, AgentId id)
    {
        var agents = world.Agents;
        for (int i = 0; i < agents.Count; i++)
            if (agents[i].Id.Equals(id))
                return agents[i];
        return null;
    }

    /// <summary>
    /// Maps a CLR payload type to the BbJsonCodec type tag string used in
    /// <see cref="PendingActuation.PayloadTypeTag"/>. Returns null for untyped completions.
    /// Extend this table if new payload types are added to the codec.
    /// </summary>
    private static string? PayloadTypeToTag(Type? t) => t switch
    {
        null => null,
        _ when t == typeof(string) => "string",
        _ when t == typeof(int) => "int",
        _ when t == typeof(long) => "long",
        _ when t == typeof(float) => "float",
        _ when t == typeof(double) => "double",
        _ when t == typeof(bool) => "bool",
        _ when t == typeof(Guid) => "guid",
        _ => null   // unsupported — treat as untyped
    };

    // ----------------- handler plumbing -----------------

    public interface IHandler
    {
        HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, IActuationCommand cmd);
    }

    public readonly record struct HandlerResult(
        bool Accepted,
        bool Completed,
        bool Ok,
        string? Error = null,
        object? Payload = null,
        Type? PayloadType = null);

    private sealed class HandlerAdapter<TCmd> : IHandler where TCmd : notnull, IActuationCommand
    {
        private readonly IActuationHandler<TCmd> _inner;
        public HandlerAdapter(IActuationHandler<TCmd> inner) => _inner = inner;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, IActuationCommand cmd)
            => _inner.Handle(host, ctx, id, (TCmd)cmd);
    }
}
