using Ariadne.OptFlow.Commands;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;

namespace Ariadne.OptFlow;

/// <summary>
/// Single-step dialogue primitives: each step dispatches a command once, then waits for completion.
/// This uses Dominatus deferred-completion semantics (completion event), NOT C# async/await.
/// </summary>
public static class DiagSteps
{
    // Base helper to normalize immediate completions in case the actuator doesn't publish events.
    // (ActuatorHost does publish; this makes the step robust against other IAiActuator impls.
    // To avoid boilerplate foreach code in diag as Csharp doesn't have "yield from",
    // This combined both the act/await step of dialogue into one clean "yield return" and replicates VN behavior expected from dialogues
    // However, a consenquence of that is that the immutable record AiStep now has mutable temp. stateid in it
    // This is benign as it's unlikely that the stateIds will be accessible.
    private static void EnsureCompletionEvents(AiCtx ctx, ActuationDispatchResult res, Type? payloadType)
    {
        if (!res.Completed) return;

        // Always publish untyped completion
        ctx.Events.Publish(new ActuationCompleted(res.Id, res.Ok, res.Error, res.Payload));

        // Publish typed completion only when requested (for Ask/Choose => string)
        if (payloadType is not null)
        {
            var completedT = typeof(ActuationCompleted<>).MakeGenericType(payloadType);
            var typedEvt = Activator.CreateInstance(
                completedT,
                new object?[] { res.Id, res.Ok, res.Error, res.Payload });

            ctx.Events.PublishObject(typedEvt!);
        }
    }

    public sealed record LineStep : AiStep, IWaitEvent
    {
        private readonly DiagLineCommand _cmd;

        private bool _started;
        private ActuationId _id;

        public LineStep(string text, string? speaker)
        {
            _cmd = new DiagLineCommand(text, speaker);
            _started = false;
            _id = default;
        }

        public bool TryConsume(AiCtx ctx, ref EventCursor cursor)
        {
            if (!_started)
            {
                var res = ctx.Act.Dispatch(ctx, _cmd);
                _id = res.Id;
                _started = true;

                // Line has no typed payload
                EnsureCompletionEvents(ctx, res, payloadType: null);
            }

            // Wait for completion of this actuation id
            return ctx.Events.TryConsume(ref cursor,
                (ActuationCompleted e) => e.Id.Equals(_id),
                out _);
        }
    }

    public sealed record AskStep : AiStep, IWaitEvent
    {
        private readonly DiagAskCommand _cmd;
        private readonly BbKey<string> _storeAs;

        private bool _started;
        private ActuationId _id;

        public AskStep(string prompt, BbKey<string> storeAs)
        {
            _cmd = new DiagAskCommand(prompt);
            _storeAs = storeAs;
            _started = false;
            _id = default;
        }

        public bool TryConsume(AiCtx ctx, ref EventCursor cursor)
        {
            if (!_started)
            {
                var res = ctx.Act.Dispatch(ctx, _cmd);
                _id = res.Id;
                _started = true;

                // Ask returns string payload
                EnsureCompletionEvents(ctx, res, payloadType: typeof(string));
            }

            if (!ctx.Events.TryConsume(ref cursor,
                    (ActuationCompleted<string> e) => e.Id.Equals(_id),
                    out var got))
                return false;

            ctx.Agent.Bb.Set(_storeAs, got.Payload ?? "");
            return true;
        }
    }

    public sealed record ChooseStep : AiStep, IWaitEvent
    {
        private readonly DiagChooseCommand _cmd;
        private readonly BbKey<string> _storeAs;

        private bool _started;
        private ActuationId _id;

        public ChooseStep(string prompt, IReadOnlyList<DiagChoice> options, BbKey<string> storeAs)
        {
            _cmd = new DiagChooseCommand(prompt, options);
            _storeAs = storeAs;
            _started = false;
            _id = default;
        }

        public bool TryConsume(AiCtx ctx, ref EventCursor cursor)
        {
            if (!_started)
            {
                var res = ctx.Act.Dispatch(ctx, _cmd);
                _id = res.Id;
                _started = true;

                // Choose returns string payload (chosen key)
                EnsureCompletionEvents(ctx, res, payloadType: typeof(string));
            }

            if (!ctx.Events.TryConsume(ref cursor,
                    (ActuationCompleted<string> e) => e.Id.Equals(_id),
                    out var got))
                return false;

            ctx.Agent.Bb.Set(_storeAs, got.Payload ?? "");
            return true;
        }
    }
}