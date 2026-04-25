using System.Text;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public static class Llm
{
    public static readonly LlmSamplingOptions DefaultSampling = new(
        Provider: "fake",
        Model: "scripted-v1",
        Temperature: 0.0);

    public static AiStep Text(
        string stableId,
        string intent,
        string persona,
        Action<LlmContextBuilder> context,
        BbKey<string> storeAs,
        LlmSamplingOptions? sampling = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableId);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(persona);
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(storeAs.Name))
        {
            throw new ArgumentException("Blackboard key must be non-empty.", nameof(storeAs));
        }

        var contextBuilder = new LlmContextBuilder();
        context(contextBuilder);
        string canonicalContextJson = contextBuilder.BuildCanonicalJson();

        var resolvedSampling = sampling ?? DefaultSampling;

        var request = new LlmTextRequest(
            StableId: stableId,
            Intent: intent,
            Persona: persona,
            CanonicalContextJson: canonicalContextJson,
            Sampling: resolvedSampling,
            PromptTemplateVersion: LlmTextRequest.DefaultPromptTemplateVersion,
            OutputContractVersion: LlmTextRequest.DefaultOutputContractVersion);

        return new LlmTextStep(request, storeAs);
    }

    private sealed record LlmTextStep(LlmTextRequest Request, BbKey<string> StoreAs) : AiStep, IWaitEvent
    {
        private readonly BbKey<bool> _completedKey = new(BuildStepKey(Request.StableId, "completed"));
        private readonly BbKey<string> _resultKey = new(BuildStepKey(Request.StableId, "result"));
        private readonly BbKey<string> _requestHashKey = new(BuildStepKey(Request.StableId, "requestHash"));
        private readonly BbKey<long> _pendingActuationIdKey = new(BuildStepKey(Request.StableId, "pendingActuationId"));

        public bool TryConsume(AiCtx ctx, ref EventCursor cursor)
        {
            if (ctx.Bb.GetOrDefault(_completedKey, false) && ctx.Bb.TryGet(_resultKey, out string? cachedResult))
            {
                ctx.Bb.Set(StoreAs, cachedResult ?? string.Empty);
                return true;
            }

            var pendingIdValue = ctx.Bb.GetOrDefault(_pendingActuationIdKey, 0L);
            if (pendingIdValue == 0)
            {
                var dispatch = ctx.Act.Dispatch(ctx, Request);

                if (!dispatch.Accepted || (dispatch.Completed && !dispatch.Ok))
                {
                    throw new InvalidOperationException(
                        $"Llm.Text dispatch failed for stableId '{Request.StableId}'. {dispatch.Error ?? "Actuation rejected."}");
                }

                ctx.Bb.Set(_pendingActuationIdKey, dispatch.Id.Value);
                ctx.Bb.Set(_requestHashKey, LlmRequestHasher.ComputeHash(Request));
                pendingIdValue = dispatch.Id.Value;
            }

            var pendingId = new ActuationId(pendingIdValue);
            if (!ctx.Events.TryConsume(
                    ref cursor,
                    (ActuationCompleted<string> e) => e.Id.Equals(pendingId),
                    out var completed))
            {
                return false;
            }

            if (!completed.Ok)
            {
                throw new InvalidOperationException(
                    $"Llm.Text completion failed for stableId '{Request.StableId}'. {completed.Error ?? "Unknown error."}");
            }

            var value = completed.Payload ?? string.Empty;
            ctx.Bb.Set(_resultKey, value);
            ctx.Bb.Set(StoreAs, value);
            ctx.Bb.Set(_completedKey, true);
            ctx.Bb.Set(_pendingActuationIdKey, 0L);
            return true;
        }

        private static string BuildStepKey(string stableId, string suffix)
            => $"llm.{SanitizeStableId(stableId)}.{suffix}";

        private static string SanitizeStableId(string stableId)
        {
            var sb = new StringBuilder(stableId.Length);

            foreach (var c in stableId)
            {
                if (char.IsLetterOrDigit(c) || c is '.' or '-' or '_')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }

            return sb.ToString();
        }
    }
}
