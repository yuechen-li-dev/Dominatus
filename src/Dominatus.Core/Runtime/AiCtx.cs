using System.Threading;

namespace Dominatus.Core.Runtime;

public readonly record struct AiCtx
{
    private readonly IAiWorldView _view;
    private readonly IAiMailbox _mail;
    private readonly IAiActuator _act;
    private readonly IAiWorldBb _worldBb;
    private readonly AiCtxSurfaceRef? _surfaces;

    public AiCtx(
        AiWorld world,
        AiAgent agent,
        AiEventBus events,
        CancellationToken cancel,
        IAiWorldView view,
        IAiMailbox mail,
        IAiActuator act,
        IAiWorldBb worldBb)
    {
        World = world;
        Agent = agent;
        Events = events;
        Cancel = cancel;
        _view = view;
        _mail = mail;
        _act = act;
        _worldBb = worldBb;
        _surfaces = null;
    }

    internal AiCtx(
        AiWorld world,
        AiAgent agent,
        AiEventBus events,
        CancellationToken cancel,
        AiCtxSurfaceRef surfaces)
    {
        World = world;
        Agent = agent;
        Events = events;
        Cancel = cancel;
        _view = surfaces.View;
        _mail = surfaces.Mail;
        _act = surfaces.Act;
        _worldBb = surfaces.WorldBb;
        _surfaces = surfaces;
    }

    public AiCtx(
        AiWorld world,
        AiAgent agent,
        AiEventBus events,
        CancellationToken cancel,
        IAiWorldView view,
        IAiMailbox mail,
        IAiActuator act)
        : this(world, agent, events, cancel, view, mail, act, new LiveWorldBb(world.Bb))
    {
    }

    public AiWorld World { get; }
    public AiAgent Agent { get; }
    public AiEventBus Events { get; }
    public CancellationToken Cancel { get; }
    public IAiWorldView View => _surfaces?.View ?? _view;
    public IAiMailbox Mail => _surfaces?.Mail ?? _mail;
    public IAiActuator Act => _surfaces?.Act ?? _act;
    public IAiWorldBb WorldBb => _surfaces?.WorldBb ?? _worldBb;

    public Blackboard.Blackboard Bb => Agent.Bb;
}
