using System.Diagnostics.CodeAnalysis;
using Dominatus.Core.Blackboard;

namespace Dominatus.Core.Runtime;

public enum ParallelWorldBbWriteKind
{
    Set,
    SetFor,
    SetUntil,
    Remove
}

public sealed record ParallelWorldBbWrite
{
    public required AgentId SourceAgentId { get; init; }
    public required string Key { get; init; }
    public required ParallelWorldBbWriteKind Kind { get; init; }
    public object? Value { get; init; }
    public Type? ValueType { get; init; }
    public float? AbsoluteTime { get; init; }
    public float? DurationSeconds { get; init; }
    public long Sequence { get; init; }
}

public sealed record ParallelMailboxMessage
{
    public required AgentId SourceAgentId { get; init; }
    public required AgentId TargetAgentId { get; init; }
    public required object Message { get; init; }
    public required Type MessageType { get; init; }
    public long Sequence { get; init; }
}

public sealed record ParallelActuationCommand
{
    public required AgentId SourceAgentId { get; init; }
    public required IActuationCommand Command { get; init; }
    public required Type CommandType { get; init; }
    public long Sequence { get; init; }
}

/// <summary>
/// Per-agent staged effect buffer for future parallel tick merge barriers.
/// Intended ownership is one agent/worker during compute; this type is not thread-safe.
/// One shared sequence is assigned across all staged effect categories to preserve authoring order.
/// </summary>
public sealed class AgentStageBuffer
{
    private readonly List<ParallelWorldBbWrite> _worldWrites = new();
    private readonly List<ParallelMailboxMessage> _mailboxMessages = new();
    private readonly List<ParallelActuationCommand> _actuations = new();
    private long _nextSequence;

    public AgentStageBuffer(AgentId sourceAgentId)
    {
        SourceAgentId = sourceAgentId;
    }

    public AgentId SourceAgentId { get; }

    public IReadOnlyList<ParallelWorldBbWrite> WorldWrites => _worldWrites;
    public IReadOnlyList<ParallelMailboxMessage> MailboxMessages => _mailboxMessages;
    public IReadOnlyList<ParallelActuationCommand> Actuations => _actuations;

    public int WorldWriteCount => _worldWrites.Count;
    public int MailboxMessageCount => _mailboxMessages.Count;
    public int ActuationCount => _actuations.Count;

    internal long NextSequence() => _nextSequence++;

    internal ParallelWorldBbWrite AddWorldWrite(ParallelWorldBbWrite write)
    {
        _worldWrites.Add(write);
        return write;
    }

    internal ParallelMailboxMessage AddMailboxMessage(ParallelMailboxMessage message)
    {
        _mailboxMessages.Add(message);
        return message;
    }

    internal ParallelActuationCommand AddActuation(ParallelActuationCommand command)
    {
        _actuations.Add(command);
        return command;
    }
}

/// <summary>
/// Stable world blackboard surface: reads only the tick snapshot and records writes into an agent buffer.
/// Staged writes are intentionally not visible to reads until a future merge publishes them into a later snapshot.
/// </summary>
public sealed class StagedWorldBb : IAiWorldBb
{
    private readonly AgentId _sourceAgentId;
    private readonly IReadOnlyDictionary<string, object?> _snapshot;
    private readonly AgentStageBuffer _stage;

    public StagedWorldBb(AgentId sourceAgentId, IReadOnlyDictionary<string, object?> snapshot, AgentStageBuffer stage)
    {
        _sourceAgentId = sourceAgentId;
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _stage = stage ?? throw new ArgumentNullException(nameof(stage));
    }

    public bool TryGet<T>(BbKey<T> key, [NotNullWhen(true)] out T? value) where T : notnull
    {
        if (_snapshot.TryGetValue(key.Name, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    public T GetOrDefault<T>(BbKey<T> key, T defaultValue) where T : notnull
        => TryGet(key, out T? value) ? value : defaultValue;

    public void Set<T>(BbKey<T> key, T value) where T : notnull
        => Record(key.Name, ParallelWorldBbWriteKind.Set, value, value.GetType(), absoluteTime: null, durationSeconds: null);

    public void SetFor<T>(BbKey<T> key, T value, float now, float ttlSeconds) where T : notnull
    {
        ValidateFinite(now, nameof(now));
        ValidateFinite(ttlSeconds, nameof(ttlSeconds));
        var absoluteTime = ttlSeconds <= 0f ? now : now + ttlSeconds;
        Record(key.Name, ParallelWorldBbWriteKind.SetFor, value, value.GetType(), absoluteTime, ttlSeconds);
    }

    public void SetUntil<T>(BbKey<T> key, T value, float expiresAt) where T : notnull
    {
        ValidateFinite(expiresAt, nameof(expiresAt));
        Record(key.Name, ParallelWorldBbWriteKind.SetUntil, value, value.GetType(), expiresAt, durationSeconds: null);
    }

    public bool Remove<T>(BbKey<T> key) where T : notnull
    {
        if (!_snapshot.ContainsKey(key.Name))
            return false;

        Record(key.Name, ParallelWorldBbWriteKind.Remove, value: null, valueType: typeof(T), absoluteTime: null, durationSeconds: null);
        return true;
    }

    private void Record(string key, ParallelWorldBbWriteKind kind, object? value, Type? valueType, float? absoluteTime, float? durationSeconds)
    {
        _stage.AddWorldWrite(new ParallelWorldBbWrite
        {
            SourceAgentId = _sourceAgentId,
            Key = key,
            Kind = kind,
            Value = value,
            ValueType = valueType,
            AbsoluteTime = absoluteTime,
            DurationSeconds = durationSeconds,
            Sequence = _stage.NextSequence()
        });
    }

    private static void ValidateFinite(float value, string paramName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(paramName, "Value must be finite.");
    }
}

/// <summary>
/// Immutable public-world view backed by tick-stable agent snapshots.
/// </summary>
public sealed class SnapshotWorldView : IAiWorldView
{
    private readonly IReadOnlyDictionary<AgentId, AgentSnapshot> _snapshotsById;
    private readonly IReadOnlyList<AgentSnapshot> _snapshotsInDeterministicOrder;

    public SnapshotWorldView(IEnumerable<AgentSnapshot> snapshots)
    {
        if (snapshots is null) throw new ArgumentNullException(nameof(snapshots));

        var ordered = snapshots
            .OrderBy(snapshot => snapshot.Id.Value)
            .ToArray();

        _snapshotsInDeterministicOrder = ordered;
        _snapshotsById = ordered.ToDictionary(snapshot => snapshot.Id);
    }

    public SnapshotWorldView(IReadOnlyDictionary<AgentId, AgentSnapshot> snapshots)
        : this((snapshots ?? throw new ArgumentNullException(nameof(snapshots))).Values)
    {
    }

    public bool TryGetAgent(AgentId id, out AgentSnapshot snapshot)
        => _snapshotsById.TryGetValue(id, out snapshot);

    public IEnumerable<AgentSnapshot> QueryAgents(Func<AgentSnapshot, bool> predicate)
    {
        if (predicate is null) throw new ArgumentNullException(nameof(predicate));

        for (int i = 0; i < _snapshotsInDeterministicOrder.Count; i++)
        {
            var snapshot = _snapshotsInDeterministicOrder[i];
            if (predicate(snapshot))
                yield return snapshot;
        }
    }
}

/// <summary>
/// Mailbox surface that records sends into an agent buffer instead of publishing to live recipient event buses.
/// Broadcast recipients are expanded against the injected stable view in deterministic AgentId order.
/// </summary>
public sealed class StagedMailbox : IAiMailbox
{
    private readonly AgentId _sourceAgentId;
    private readonly AgentStageBuffer _stage;
    private readonly IAiWorldView _stableView;

    public StagedMailbox(AgentId sourceAgentId, AgentStageBuffer stage, IAiWorldView stableView)
    {
        _sourceAgentId = sourceAgentId;
        _stage = stage ?? throw new ArgumentNullException(nameof(stage));
        _stableView = stableView ?? throw new ArgumentNullException(nameof(stableView));
    }

    public bool Send<T>(AgentId to, T message) where T : notnull
    {
        if (!_stableView.TryGetAgent(to, out _))
            return false;

        Record(to, message, message.GetType());
        return true;
    }

    public int Broadcast<T>(Func<AgentSnapshot, bool> recipients, T message) where T : notnull
    {
        if (recipients is null) throw new ArgumentNullException(nameof(recipients));

        var targets = _stableView.QueryAgents(recipients)
            .OrderBy(snapshot => snapshot.Id.Value)
            .ToArray();

        for (int i = 0; i < targets.Length; i++)
            Record(targets[i].Id, message, message.GetType());

        return targets.Length;
    }

    private void Record<T>(AgentId to, T message, Type messageType) where T : notnull
    {
        _stage.AddMailboxMessage(new ParallelMailboxMessage
        {
            SourceAgentId = _sourceAgentId,
            TargetAgentId = to,
            Message = message,
            MessageType = messageType,
            Sequence = _stage.NextSequence()
        });
    }
}

/// <summary>
/// Actuator surface that records actuation commands for a future merge/dispatch barrier.
/// It does not evaluate policies, call handlers, or publish completion events.
/// </summary>
public sealed class StagedActuator : IAiActuator
{
    private readonly AgentId _sourceAgentId;
    private readonly AgentStageBuffer _stage;

    public StagedActuator(AgentId sourceAgentId, AgentStageBuffer stage)
    {
        _sourceAgentId = sourceAgentId;
        _stage = stage ?? throw new ArgumentNullException(nameof(stage));
    }

    public ActuationDispatchResult Dispatch(AiCtx ctx, IActuationCommand command)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var sequence = _stage.NextSequence();
        _stage.AddActuation(new ParallelActuationCommand
        {
            SourceAgentId = _sourceAgentId,
            Command = command,
            CommandType = command.GetType(),
            Sequence = sequence
        });

        return new ActuationDispatchResult(
            Id: new ActuationId(sequence + 1),
            Accepted: true,
            Completed: false,
            Ok: true);
    }
}
