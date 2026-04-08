using Ariadne.OptFlow.Commands;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;

namespace Ariadne.OptFlow;

/// <summary>
/// Single-step dialogue primitives: each step dispatches a command once, then waits for completion.
/// Uses Dominatus deferred-completion semantics (ActuationCompleted event) — NOT C# async/await.
///
/// <para>
/// <b>Restore semantics (M5c — Option A: BB-scoped synthetic keys):</b>
/// The original implementation stored <c>_started</c> and <c>_id</c> as mutable fields on the
/// step instance. Because <see cref="NodeRunner"/> holds an <c>IEnumerator</c> that is never
/// serialized, a cold restore (checkpoint + replay) re-allocates the step with <c>_started = false</c>,
/// causing the actuation to be re-dispatched — showing a duplicate line or re-prompting a choice
/// the player already answered.
/// </para>
/// <para>
/// Fix: each step derives a stable <c>BbKey&lt;long&gt;</c> from its callsite identity string
/// (prefixed <c>__diag.</c> to avoid collisions with user keys). The actuation id is written to
/// the BB on first dispatch and read back on re-entry. Because the BB is fully restored before
/// the HFSM re-enters nodes, a restored step finds its id already present and skips re-dispatch,
/// waiting only for the completion event that the replay driver will re-inject.
/// </para>
/// <para>
/// Callsite ids must be unique within a dialogue node. By convention pass a short literal string
/// matching the variable name or line position, e.g. <c>"intro"</c>, <c>"askName"</c>.
/// Duplicate ids within the same node will alias to the same BB key — a debug assertion guards
/// this in future (M5c tracker).
/// </para>
/// </summary>
public static class DiagSteps
{
    // Prefix convention: all synthetic diag keys begin with "__diag." to avoid collisions
    // with user-defined BB keys. BbJsonCodec will snapshot and restore these transparently.
    private const string KeyPrefix = "__diag.";

    /// <summary>
    /// Returns the BB key used to persist the pending <see cref="ActuationId"/> for a step.
    /// The underlying type is <c>long</c> because <c>ActuationId</c> is not in the codec type
    /// table, but <c>long</c> is. Reconstruct via <c>new ActuationId(value)</c>.
    /// </summary>
    private static BbKey<long> PendingIdKey(string callsiteId)
        => new($"{KeyPrefix}{callsiteId}.pendingId");

    /// <summary>
    /// Returns the BB key used to track whether a step has been dispatched.
    /// Stored as <c>bool</c> rather than checking <c>pendingId != 0</c> to be explicit —
    /// a zero ActuationId is theoretically valid in some actuator implementations.
    /// </summary>
    private static BbKey<bool> StartedKey(string callsiteId)
        => new($"{KeyPrefix}{callsiteId}.started");

    // Normalise immediate completions so steps are robust against IAiActuator impls
    // that complete synchronously without publishing events.
    private static void EnsureCompletionEvents(AiCtx ctx, ActuationDispatchResult res, Type? payloadType)
    {
        if (!res.Completed) return;

        ctx.Events.Publish(new ActuationCompleted(res.Id, res.Ok, res.Error, res.Payload));

        if (payloadType is not null)
        {
            var completedT = typeof(ActuationCompleted<>).MakeGenericType(payloadType);
            var typedEvt = Activator.CreateInstance(
                completedT,
                new object?[] { res.Id, res.Ok, res.Error, res.Payload });

            ctx.Events.PublishObject(typedEvt!);
        }
    }

    /// <summary>
    /// Displays a line of dialogue and waits for the player to advance.
    /// </summary>
    /// <param name="callsiteId">
    /// Stable unique string identifying this step within its dialogue node.
    /// Used to derive BB keys for restore. Must be unique per node; use a short literal.
    /// </param>
    public sealed record LineStep(string Text, string? Speaker, string CallsiteId) : AiStep, IWaitEvent
    {
        private readonly DiagLineCommand _cmd = new(Text, Speaker);

        public bool TryConsume(AiCtx ctx, ref EventCursor cursor)
        {
            var startedKey = StartedKey(CallsiteId);
            var pendingIdKey = PendingIdKey(CallsiteId);

            if (!ctx.Bb.GetOrDefault(startedKey, false))
            {
                var res = ctx.Act.Dispatch(ctx, _cmd);
                ctx.Bb.Set(pendingIdKey, res.Id.Value);
                ctx.Bb.Set(startedKey, true);
                EnsureCompletionEvents(ctx, res, payloadType: null);
            }

            var id = new ActuationId(ctx.Bb.GetOrDefault(pendingIdKey, 0L));

            if (!ctx.Events.TryConsume(ref cursor,
                    (ActuationCompleted e) => e.Id.Equals(id),
                    out _))
                return false;

            // Step completed successfully — clear restore bookkeeping so this same
            // callsite can be used again later in a loop/menu without reusing stale ids.
            ctx.Bb.Set(startedKey, false);
            ctx.Bb.Set(pendingIdKey, 0L);
            return true;
        }
    }

    /// <summary>
    /// Prompts the player for free-text input and stores the result in <paramref name="storeAs"/>.
    /// </summary>
    /// <param name="callsiteId">Stable unique string identifying this step within its dialogue node.</param>
    public sealed record AskStep(string Prompt, BbKey<string> StoreAs, string CallsiteId) : AiStep, IWaitEvent
    {
        private readonly DiagAskCommand _cmd = new(Prompt);

        public bool TryConsume(AiCtx ctx, ref EventCursor cursor)
        {
            var startedKey = StartedKey(CallsiteId);
            var pendingIdKey = PendingIdKey(CallsiteId);

            if (!ctx.Bb.GetOrDefault(startedKey, false))
            {
                var res = ctx.Act.Dispatch(ctx, _cmd);
                ctx.Bb.Set(pendingIdKey, res.Id.Value);
                ctx.Bb.Set(startedKey, true);
                EnsureCompletionEvents(ctx, res, payloadType: typeof(string));
            }

            var id = new ActuationId(ctx.Bb.GetOrDefault(pendingIdKey, 0L));

            if (!ctx.Events.TryConsume(ref cursor,
                    (ActuationCompleted<string> e) => e.Id.Equals(id),
                    out var got))
                return false;

            ctx.Bb.Set(StoreAs, got.Payload ?? "");

            // Step completed successfully — clear restore bookkeeping so this same
            // callsite can be used again later in a loop/menu without reusing stale ids.
            ctx.Bb.Set(startedKey, false);
            ctx.Bb.Set(pendingIdKey, 0L);
            return true;
        }
    }

    /// <summary>
    /// Presents a set of choices to the player and stores the selected key in <paramref name="storeAs"/>.
    /// </summary>
    /// <param name="callsiteId">Stable unique string identifying this step within its dialogue node.</param>
    public sealed record ChooseStep(
        string Prompt,
        IReadOnlyList<DiagChoice> Options,
        BbKey<string> StoreAs,
        string CallsiteId) : AiStep, IWaitEvent
    {
        private readonly DiagChooseCommand _cmd = new(Prompt, Options);

        public bool TryConsume(AiCtx ctx, ref EventCursor cursor)
        {
            var startedKey = StartedKey(CallsiteId);
            var pendingIdKey = PendingIdKey(CallsiteId);

            if (!ctx.Bb.GetOrDefault(startedKey, false))
            {
                var res = ctx.Act.Dispatch(ctx, _cmd);
                ctx.Bb.Set(pendingIdKey, res.Id.Value);
                ctx.Bb.Set(startedKey, true);
                EnsureCompletionEvents(ctx, res, payloadType: typeof(string));
            }

            var id = new ActuationId(ctx.Bb.GetOrDefault(pendingIdKey, 0L));

            if (!ctx.Events.TryConsume(ref cursor,
                    (ActuationCompleted<string> e) => e.Id.Equals(id),
                    out var got))
                return false;

            ctx.Bb.Set(StoreAs, got.Payload ?? "");

            // Step completed successfully — clear restore bookkeeping so this same
            // callsite can be used again later in a loop/menu without reusing stale ids.
            ctx.Bb.Set(startedKey, false);
            ctx.Bb.Set(pendingIdKey, 0L);
            return true;
        }
    }
}