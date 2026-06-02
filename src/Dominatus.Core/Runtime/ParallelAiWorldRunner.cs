using System.Collections.Concurrent;
using Dominatus.Core.Blackboard;

namespace Dominatus.Core.Runtime;

public enum ParallelWorldWriteConflictPolicy
{
    Fail,
    LastWriterByAgentId,
    FirstWriterByAgentId
}

public sealed record ParallelTickOptions
{
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;
    public ParallelWorldWriteConflictPolicy WorldWriteConflictPolicy { get; init; } = ParallelWorldWriteConflictPolicy.Fail;
    public bool AdvanceWorldClock { get; init; } = true;
    public bool ExpireWorldBlackboard { get; init; } = true;
    public bool TickActuator { get; init; } = true;
    public Func<AiAgent, bool>? AgentFilter { get; init; }
    public Action<AiAgent>? AgentTickStarting { get; init; }
    public Action<AiAgent>? AgentTickCompleted { get; init; }

    internal void Validate()
    {
        if (MaxDegreeOfParallelism < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxDegreeOfParallelism), "MaxDegreeOfParallelism must be at least 1.");
    }
}

public sealed record ParallelTickConflict
{
    public required string Key { get; init; }
    public required IReadOnlyList<AgentId> WriterAgentIds { get; init; }
    public required string? ValueTypeName { get; init; }
}

public sealed class ParallelTickConflictException : Exception
{
    public ParallelTickConflictException(IReadOnlyList<ParallelTickConflict> conflicts)
        : base(CreateMessage(conflicts))
    {
        Conflicts = conflicts ?? throw new ArgumentNullException(nameof(conflicts));
    }

    public IReadOnlyList<ParallelTickConflict> Conflicts { get; }

    private static string CreateMessage(IReadOnlyList<ParallelTickConflict> conflicts)
        => conflicts.Count == 1
            ? $"Parallel world blackboard write conflict on key '{conflicts[0].Key}'."
            : $"Parallel world blackboard write conflicts on {conflicts.Count} keys.";
}

public sealed record ParallelTickResult
{
    public required int AgentsTicked { get; init; }
    public required int WorldWritesStaged { get; init; }
    public required int WorldWritesCommitted { get; init; }
    public required int MailboxMessagesStaged { get; init; }
    public required int MailboxMessagesDelivered { get; init; }
    public required int ActuationsStaged { get; init; }
    public required int ActuationsDispatched { get; init; }
    public required IReadOnlyList<ParallelTickConflict> Conflicts { get; init; }
    public required int MaxDegreeOfParallelism { get; init; }
}

/// <summary>
/// Core safe-subset staged parallel tick runner.
/// <para>
/// Semantics are: stable tick-N read view, independent agent compute, per-agent staged effects,
/// deterministic merge barrier, visibility on tick N+1. Authored code must use ctx.WorldBb,
/// ctx.Mail, ctx.Act/Ai.Act, ctx.View, and ctx.Bb for parallel-safe effects. The live ctx.World
/// object remains available as an escape hatch for M4 and is not made parallel-safe by this runner.
/// </para>
/// </summary>
public sealed class ParallelAiWorldRunner
{
    public ParallelTickResult Tick(
        AiWorld world,
        float dt,
        ParallelTickOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        var effectiveOptions = options ?? new ParallelTickOptions();
        effectiveOptions.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        Prepare(world, dt, effectiveOptions);

        var agents = world.Agents
            .Where(agent => effectiveOptions.AgentFilter?.Invoke(agent) ?? true)
            .OrderBy(agent => agent.Id.Value)
            .ToArray();
        var agentLookup = agents.ToDictionary(agent => agent.Id);
        var publicSnapshots = world.SnapshotPublicAgents();
        var view = new SnapshotWorldView(publicSnapshots);
        var worldBbSnapshot = world.Bb.SnapshotValues();

        var buffers = new AgentStageBuffer[agents.Length];
        var faults = new ConcurrentQueue<Exception>();

        try
        {
            Parallel.ForEach(
                Enumerable.Range(0, agents.Length),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = effectiveOptions.MaxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                },
                index =>
                {
                    var agent = agents[index];
                    var stage = new AgentStageBuffer(agent.Id);
                    buffers[index] = stage;

                    AiCtxFactory factory = (w, a, cancel) => new AiCtx(
                        w,
                        a,
                        a.Events,
                        cancel,
                        view,
                        new StagedMailbox(a.Id, stage, view),
                        new StagedActuator(a.Id, stage),
                        new StagedWorldBb(a.Id, worldBbSnapshot, stage));

                    agent.Brain.ContextFactory = factory;
                    try
                    {
                        effectiveOptions.AgentTickStarting?.Invoke(agent);
                        agent.Tick(world);
                        effectiveOptions.AgentTickCompleted?.Invoke(agent);
                    }
                    catch (Exception ex)
                    {
                        faults.Enqueue(new ParallelAgentTickException(agent.Id, ex));
                    }
                    finally
                    {
                        agent.Brain.ContextFactory = null;
                    }
                });
        }
        catch (OperationCanceledException)
        {
            ClearContextFactories(agents);
            throw;
        }

        ClearContextFactories(agents);

        if (!faults.IsEmpty)
            throw new AggregateException(faults);

        cancellationToken.ThrowIfCancellationRequested();

        var orderedBuffers = buffers
            .Where(buffer => buffer is not null)
            .OrderBy(buffer => buffer.SourceAgentId.Value)
            .ToArray();

        var worldWrites = orderedBuffers.SelectMany(buffer => buffer.WorldWrites).ToArray();
        var mailboxMessages = orderedBuffers.SelectMany(buffer => buffer.MailboxMessages).ToArray();
        var actuations = orderedBuffers.SelectMany(buffer => buffer.Actuations).ToArray();

        var (committedWrites, conflicts) = CommitWorldWrites(world, worldWrites, effectiveOptions.WorldWriteConflictPolicy);
        cancellationToken.ThrowIfCancellationRequested();

        var deliveredMessages = DeliverMailboxMessages(mailboxMessages, agentLookup);
        cancellationToken.ThrowIfCancellationRequested();

        var dispatchedActuations = DispatchActuations(world, actuations, agentLookup, cancellationToken);

        return new ParallelTickResult
        {
            AgentsTicked = agents.Length,
            WorldWritesStaged = worldWrites.Length,
            WorldWritesCommitted = committedWrites,
            MailboxMessagesStaged = mailboxMessages.Length,
            MailboxMessagesDelivered = deliveredMessages,
            ActuationsStaged = actuations.Length,
            ActuationsDispatched = dispatchedActuations,
            Conflicts = conflicts,
            MaxDegreeOfParallelism = effectiveOptions.MaxDegreeOfParallelism
        };
    }

    private static void Prepare(AiWorld world, float dt, ParallelTickOptions options)
    {
        if (options.AdvanceWorldClock)
            world.Clock.Advance(dt);
        if (options.ExpireWorldBlackboard)
            world.Bb.Expire(world.Clock.Time);

        if (options.TickActuator && world.Actuator is ITickableActuator tickable)
            tickable.Tick(world);
    }

    private static void ClearContextFactories(IEnumerable<AiAgent> agents)
    {
        foreach (var agent in agents)
            agent.Brain.ContextFactory = null;
    }

    private static (int Committed, IReadOnlyList<ParallelTickConflict> Conflicts) CommitWorldWrites(
        AiWorld world,
        IReadOnlyList<ParallelWorldBbWrite> writes,
        ParallelWorldWriteConflictPolicy policy)
    {
        if (writes.Count == 0)
            return (0, Array.Empty<ParallelTickConflict>());

        var perAgentFinalWrites = writes
            .GroupBy(write => new { write.Key, write.SourceAgentId })
            .Select(group => group.OrderBy(write => write.Sequence).Last())
            .ToArray();

        var conflicts = perAgentFinalWrites
            .GroupBy(write => write.Key, StringComparer.Ordinal)
            .Select(group => new
            {
                Key = group.Key,
                Writes = group.OrderBy(write => write.SourceAgentId.Value).ToArray(),
                WriterIds = group.Select(write => write.SourceAgentId).Distinct().OrderBy(id => id.Value).ToArray()
            })
            .Where(group => group.WriterIds.Length > 1)
            .Select(group => new ParallelTickConflict
            {
                Key = group.Key,
                WriterAgentIds = group.WriterIds,
                ValueTypeName = group.Writes.Select(write => write.ValueType?.FullName).FirstOrDefault(name => name is not null)
            })
            .ToArray();

        if (conflicts.Length > 0 && policy == ParallelWorldWriteConflictPolicy.Fail)
            throw new ParallelTickConflictException(conflicts);

        var winners = perAgentFinalWrites
            .GroupBy(write => write.Key, StringComparer.Ordinal)
            .Select(group => SelectWinner(group, policy))
            .OrderBy(write => write.SourceAgentId.Value)
            .ThenBy(write => write.Sequence)
            .ToArray();

        foreach (var write in winners)
            CommitWorldWrite(world.Bb, write);

        return (winners.Length, conflicts);
    }

    private static ParallelWorldBbWrite SelectWinner(IEnumerable<ParallelWorldBbWrite> writes, ParallelWorldWriteConflictPolicy policy)
    {
        var ordered = writes.OrderBy(write => write.SourceAgentId.Value).ThenBy(write => write.Sequence).ToArray();
        return policy == ParallelWorldWriteConflictPolicy.LastWriterByAgentId ? ordered[^1] : ordered[0];
    }

    private static void CommitWorldWrite(Blackboard.Blackboard bb, ParallelWorldBbWrite write)
    {
        switch (write.Kind)
        {
            case ParallelWorldBbWriteKind.Set:
                bb.SetObject(write.Key, write.Value);
                break;
            case ParallelWorldBbWriteKind.SetFor:
            case ParallelWorldBbWriteKind.SetUntil:
                bb.SetUntilObject(write.Key, write.Value, write.AbsoluteTime ?? throw new InvalidOperationException($"Write for '{write.Key}' has no absolute expiry."));
                break;
            case ParallelWorldBbWriteKind.Remove:
                bb.RemoveObject(write.Key);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(write.Kind));
        }
    }

    private static int DeliverMailboxMessages(IReadOnlyList<ParallelMailboxMessage> messages, IReadOnlyDictionary<AgentId, AiAgent> agents)
    {
        var delivered = 0;
        foreach (var message in messages
            .OrderBy(message => message.SourceAgentId.Value)
            .ThenBy(message => message.Sequence)
            .ThenBy(message => message.TargetAgentId.Value))
        {
            if (!agents.TryGetValue(message.TargetAgentId, out var target))
                continue;

            target.Events.PublishObject(message.Message, message.MessageType);
            delivered++;
        }

        return delivered;
    }

    private static int DispatchActuations(
        AiWorld world,
        IReadOnlyList<ParallelActuationCommand> actuations,
        IReadOnlyDictionary<AgentId, AiAgent> agents,
        CancellationToken cancellationToken)
    {
        var dispatched = 0;
        foreach (var actuation in actuations
            .OrderBy(actuation => actuation.SourceAgentId.Value)
            .ThenBy(actuation => actuation.Sequence)
            .ThenBy(actuation => actuation.CommandType.FullName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!agents.TryGetValue(actuation.SourceAgentId, out var source))
                continue;

            var ctx = AiCtxFactories.Live(world, source, cancellationToken);
            world.Actuator.Dispatch(ctx, actuation.Command);
            dispatched++;
        }

        return dispatched;
    }
}

public sealed class ParallelAgentTickException : Exception
{
    public ParallelAgentTickException(AgentId agentId, Exception innerException)
        : base($"Parallel agent tick failed for agent {agentId.Value}.", innerException)
    {
        AgentId = agentId;
    }

    public AgentId AgentId { get; }
}
