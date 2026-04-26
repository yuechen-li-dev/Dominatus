using Dominatus.Core.Runtime;

namespace Dominatus.Actuators.Standard;

public interface IStandardSystemClock
{
    DateTimeOffset UtcNow { get; }
    DateTimeOffset LocalNow { get; }
}

public sealed class StandardSystemClock : IStandardSystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public DateTimeOffset LocalNow => DateTimeOffset.Now;
}

public sealed record GetUtcNowCommand() : IActuationCommand;

public sealed record GetLocalNowCommand() : IActuationCommand;

public sealed record TimeResult(DateTimeOffset Timestamp);

public sealed class TimeActuationHandler :
    IActuationHandler<GetUtcNowCommand>,
    IActuationHandler<GetLocalNowCommand>
{
    private readonly IStandardSystemClock _clock;

    public TimeActuationHandler(IStandardSystemClock? clock = null)
        => _clock = clock ?? new StandardSystemClock();

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, GetUtcNowCommand cmd)
        => ActuatorHost.HandlerResult.CompletedWithPayload(new TimeResult(_clock.UtcNow));

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, GetLocalNowCommand cmd)
        => ActuatorHost.HandlerResult.CompletedWithPayload(new TimeResult(_clock.LocalNow));
}

public static class StandardActuatorRegistration
{
    public static ActuatorHost RegisterStandardFileActuators(this ActuatorHost host, SandboxedFileActuatorOptions options)
    {
        if (host is null)
            throw new ArgumentNullException(nameof(host));

        var handler = new SandboxedFileActuationHandler(options);
        host.Register<ReadTextFileCommand>(handler);
        host.Register<WriteTextFileCommand>(handler);
        host.Register<AppendTextFileCommand>(handler);
        host.Register<FileExistsCommand>(handler);
        host.Register<ListFilesCommand>(handler);
        return host;
    }

    public static ActuatorHost RegisterStandardTimeActuators(this ActuatorHost host, IStandardSystemClock? clock = null)
    {
        if (host is null)
            throw new ArgumentNullException(nameof(host));

        var handler = new TimeActuationHandler(clock);
        host.Register<GetUtcNowCommand>(handler);
        host.Register<GetLocalNowCommand>(handler);
        return host;
    }
}
