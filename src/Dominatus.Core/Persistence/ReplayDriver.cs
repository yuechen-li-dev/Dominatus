using Dominatus.Core.Runtime;

namespace Dominatus.Core.Persistence;

/// <summary>
/// Feeds a <see cref="ReplayLog"/> back into a <see cref="AiWorld"/> deterministically,
/// reconstructing the nondeterministic inputs that occurred after a checkpoint.
/// <para>
/// <b>Architecture note (Dominatus tool-call ABI):</b>
/// Actuation is the typed tool-call layer of the Dominatus kernel. Each
/// <see cref="IActuationCommand"/> is a skill an agent can invoke — equivalent to an LLM
/// tool call, but fully deterministic when replayed. The <see cref="ReplayDriver"/> is what
/// makes this pipeline auditable: every nondeterministic input (player advance, text entry,
/// choice, external signal) is logged as a <see cref="ReplayEvent"/> and can be replayed
/// exactly, whether the planner is an HFSM, a utility scorer, or an LLM decision policy.
/// </para>
/// <para>
/// <b>Replay strategy:</b>
/// <list type="bullet">
///   <item>
///     <see cref="ReplayEvent.Advance"/> → publishes an untyped <see cref="ActuationCompleted"/>
///     for the pending Line actuation on the named agent.
///   </item>
///   <item>
///     <see cref="ReplayEvent.Text"/> → publishes <see cref="ActuationCompleted"/> and
///     <see cref="ActuationCompleted{T}"/> (string) for the pending Ask actuation.
///   </item>
///   <item>
///     <see cref="ReplayEvent.Choice"/> → same as Text but for Choose actuation.
///   </item>
///   <item>
///     <see cref="ReplayEvent.External"/> → published as a raw <see cref="ExternalReplayEvent"/>
///     into the named agent's bus. The host can subscribe to this type to drive domain logic.
///   </item>
///   <item>
///     <see cref="ReplayEvent.RngSeed"/> → no-op until RNG is wired into <see cref="AiWorld"/>.
///   </item>
/// </list>
/// </para>
/// <para>
/// <b>Pending actuation ids:</b>
/// The driver resolves actuation ids from <see cref="EventCursorSnapshot.Pending"/> — the
/// list of in-flight actuations captured at checkpoint time. Replay events are matched to
/// pending actuations in log order. If the log contains more events than pending actuations
/// (i.e. replay extends beyond the checkpoint window), the driver uses a monotonically
/// incrementing synthetic id starting at <see cref="SyntheticIdStart"/>.
/// </para>
/// </summary>
public sealed class ReplayDriver
{
    /// <summary>
    /// Synthetic actuation ids used for replay events that fall outside the checkpoint's
    /// pending actuation list. Chosen to be far from real ids to avoid accidental collisions.
    /// </summary>
    public const long SyntheticIdStart = long.MaxValue - 1_000_000L;

    private readonly AiWorld _world;
    private readonly ReplayLog _log;

    // Per-agent pending actuation queues, loaded from EventCursorSnapshot.
    private readonly Dictionary<string, Queue<PendingActuation>> _pending = new();

    private long _syntheticId = SyntheticIdStart;

    /// <summary>Index of the next event in <see cref="ReplayLog.Events"/> to be applied.</summary>
    public int Cursor { get; private set; }

    /// <summary>True when all log events have been applied.</summary>
    public bool IsComplete => Cursor >= _log.Events.Length;

    public ReplayDriver(AiWorld world, ReplayLog log, EventCursorSnapshot[] agentCursors)
    {
        _world = world;
        _log   = log;

        // Build per-agent pending queues from checkpoint cursor snapshots.
        // agentCursors is parallel to world.Agents (matched by index, same as DominatusCheckpointBuilder).
        for (int i = 0; i < agentCursors.Length && i < world.Agents.Count; i++)
        {
            var agentId = world.Agents[i].Id.ToString();
            _pending[agentId] = new Queue<PendingActuation>(agentCursors[i].Pending);
        }
    }

    /// <summary>
    /// Applies all remaining log events immediately.
    /// Use this for a fast-forward restore (load → replay all → tick).
    /// </summary>
    public void ApplyAll()
    {
        while (!IsComplete)
            ApplyNext();
    }

    /// <summary>
    /// Applies the next single log event and advances <see cref="Cursor"/>.
    /// Use this for step-by-step replay or debugging.
    /// </summary>
    public void ApplyNext()
    {
        if (IsComplete) return;
        Apply(_log.Events[Cursor]);
        Cursor++;
    }

    /// <summary>
    /// Applies all log events up to (but not including) <paramref name="targetCursor"/>.
    /// Useful for seeking to a specific replay position.
    /// </summary>
    public void ApplyUpTo(int targetCursor)
    {
        targetCursor = Math.Min(targetCursor, _log.Events.Length);
        while (Cursor < targetCursor)
            ApplyNext();
    }

    // -----------------------------------------------------------------------
    // Internal dispatch
    // -----------------------------------------------------------------------

    private void Apply(ReplayEvent evt)
    {
        switch (evt)
        {
            case ReplayEvent.Advance e:
                ApplyAdvance(e.AgentId);
                break;

            case ReplayEvent.Text e:
                ApplyText(e.AgentId, e.Value);
                break;

            case ReplayEvent.Choice e:
                ApplyChoice(e.AgentId, e.ChoiceKey);
                break;

            case ReplayEvent.External e:
                ApplyExternal(e.AgentId, e.Type, e.JsonPayload);
                break;

            case ReplayEvent.RngSeed:
                // No-op until RNG is wired into AiWorld. Seed is stored in the log
                // for future use — replay is deterministic without it for now.
                break;
        }
    }

    /// <summary>
    /// Advance = player dismissed a dialogue line.
    /// Publishes untyped <see cref="ActuationCompleted"/> for the pending Line actuation.
    /// </summary>
    private void ApplyAdvance(string agentId)
    {
        var agent = FindAgent(agentId);
        if (agent is null) return;

        var id = NextId(agentId);
        agent.Events.Publish(new ActuationCompleted(id, Ok: true));
    }

    /// <summary>
    /// Text = player entered free text (Ask response).
    /// Publishes both untyped and typed (<see cref="ActuationCompleted{T}"/> string) completions.
    /// </summary>
    private void ApplyText(string agentId, string value)
    {
        var agent = FindAgent(agentId);
        if (agent is null) return;

        var id = NextId(agentId);
        agent.Events.Publish(new ActuationCompleted(id, Ok: true, Payload: value));
        agent.Events.Publish(new ActuationCompleted<string>(id, Ok: true, Payload: value));
    }

    /// <summary>
    /// Choice = player selected a dialogue option.
    /// Publishes both untyped and typed (<see cref="ActuationCompleted{T}"/> string) completions.
    /// </summary>
    private void ApplyChoice(string agentId, string choiceKey)
    {
        var agent = FindAgent(agentId);
        if (agent is null) return;

        var id = NextId(agentId);
        agent.Events.Publish(new ActuationCompleted(id, Ok: true, Payload: choiceKey));
        agent.Events.Publish(new ActuationCompleted<string>(id, Ok: true, Payload: choiceKey));
    }

    /// <summary>
    /// External = host-defined signal (e.g. "DoorOpened").
    /// Published as <see cref="ExternalReplayEvent"/> into the agent's bus.
    /// The host subscribes to this type to drive domain logic during replay.
    /// </summary>
    private void ApplyExternal(string agentId, string type, string jsonPayload)
    {
        var agent = FindAgent(agentId);
        if (agent is null) return;

        agent.Events.Publish(new ExternalReplayEvent(type, jsonPayload));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private AiAgent? FindAgent(string agentId)
        => _world.Agents.FirstOrDefault(a => a.Id.ToString() == agentId);

    /// <summary>
    /// Returns the next pending <see cref="ActuationId"/> for <paramref name="agentId"/>.
    /// Falls back to a synthetic id if the pending queue is exhausted (replay extends
    /// beyond the checkpoint window, or cursor snapshot was incomplete).
    /// </summary>
    private ActuationId NextId(string agentId)
    {
        if (_pending.TryGetValue(agentId, out var queue) && queue.Count > 0)
            return new ActuationId(queue.Dequeue().ActuationIdValue);

        return new ActuationId(_syntheticId--);
    }
}
