using System.Text;
using System.Text.Json;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public static class Llm
{
    public const string LineSpeakerContextKey = "__speaker";
    public const string NarrateNarratorContextKey = "__narrator";
    public const string NarrateStyleContextKey = "__narrationStyle";
    public const string ReplySpeakerContextKey = "__replySpeaker";
    public const string ReplyInputContextKey = "__replyInput";

    public static readonly LlmSamplingOptions DefaultSampling = new(
        Provider: "fake",
        Model: "scripted-v1",
        Temperature: 0.0);

    public static AiStep Line(
        string stableId,
        string speaker,
        string intent,
        string persona,
        Action<LlmContextBuilder> context,
        BbKey<string> storeAs,
        LlmSamplingOptions? sampling = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableId);
        ArgumentException.ThrowIfNullOrWhiteSpace(speaker);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(persona);
        ArgumentNullException.ThrowIfNull(context);

        return Text(
            stableId: stableId,
            intent: intent,
            persona: persona,
            context: builder =>
            {
                builder.Add(LineSpeakerContextKey, speaker);

                try
                {
                    context(builder);
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.Contains($"'{LineSpeakerContextKey}'", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Llm.Line reserves context key '{LineSpeakerContextKey}'. " +
                        "Use a different caller context key for speaker-related data.",
                        ex);
                }
            },
            storeAs: storeAs,
            sampling: sampling);
    }

    public static AiStep Narrate(
        string stableId,
        string intent,
        string narrator,
        string style,
        Action<LlmContextBuilder> context,
        BbKey<string> storeAs,
        LlmSamplingOptions? sampling = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableId);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(narrator);
        ArgumentException.ThrowIfNullOrWhiteSpace(style);
        ArgumentNullException.ThrowIfNull(context);

        string persona = $"Narrator: {narrator}\nNarration style: {style}";

        return Text(
            stableId: stableId,
            intent: intent,
            persona: persona,
            context: builder =>
            {
                builder
                    .Add(NarrateNarratorContextKey, narrator)
                    .Add(NarrateStyleContextKey, style);

                try
                {
                    context(builder);
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.Contains($"'{NarrateNarratorContextKey}'", StringComparison.Ordinal) ||
                    ex.Message.Contains($"'{NarrateStyleContextKey}'", StringComparison.Ordinal))
                {
                    var reservedKey = ex.Message.Contains($"'{NarrateNarratorContextKey}'", StringComparison.Ordinal)
                        ? NarrateNarratorContextKey
                        : NarrateStyleContextKey;

                    throw new InvalidOperationException(
                        $"Llm.Narrate reserves context key '{reservedKey}'. " +
                        "Use different caller context keys for narration metadata.",
                        ex);
                }
            },
            storeAs: storeAs,
            sampling: sampling);
    }

    public static AiStep Reply(
        string stableId,
        string speaker,
        string intent,
        string persona,
        string input,
        Action<LlmContextBuilder> context,
        BbKey<string> storeAs,
        LlmSamplingOptions? sampling = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableId);
        ArgumentException.ThrowIfNullOrWhiteSpace(speaker);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(persona);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentNullException.ThrowIfNull(context);

        return Text(
            stableId: stableId,
            intent: intent,
            persona: persona,
            context: builder =>
            {
                builder
                    .Add(ReplySpeakerContextKey, speaker)
                    .Add(ReplyInputContextKey, input);

                try
                {
                    context(builder);
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.Contains($"'{ReplySpeakerContextKey}'", StringComparison.Ordinal) ||
                    ex.Message.Contains($"'{ReplyInputContextKey}'", StringComparison.Ordinal))
                {
                    var reservedKey = ex.Message.Contains($"'{ReplySpeakerContextKey}'", StringComparison.Ordinal)
                        ? ReplySpeakerContextKey
                        : ReplyInputContextKey;

                    throw new InvalidOperationException(
                        $"Llm.Reply reserves context key '{reservedKey}'. " +
                        "Use different caller context keys for reply metadata.",
                        ex);
                }
            },
            storeAs: storeAs,
            sampling: sampling);
    }

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

    public static LlmDecisionOption Option(string id, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        return new LlmDecisionOption(id, description);
    }

    public static AiStep Decide(
        string stableId,
        string intent,
        string persona,
        Action<LlmContextBuilder> context,
        IReadOnlyList<LlmDecisionOption> options,
        BbKey<string> storeChosenAs,
        BbKey<string>? storeRationaleAs = null,
        BbKey<string>? storeResultJsonAs = null,
        LlmSamplingOptions? sampling = null,
        LlmDecisionPolicy? policy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableId);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(persona);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(storeChosenAs.Name))
        {
            throw new ArgumentException("Blackboard key must be non-empty.", nameof(storeChosenAs));
        }

        if (storeRationaleAs is BbKey<string> rationaleKey && string.IsNullOrWhiteSpace(rationaleKey.Name))
        {
            throw new ArgumentException("Blackboard key must be non-empty.", nameof(storeRationaleAs));
        }

        if (storeResultJsonAs is BbKey<string> resultJsonKey && string.IsNullOrWhiteSpace(resultJsonKey.Name))
        {
            throw new ArgumentException("Blackboard key must be non-empty.", nameof(storeResultJsonAs));
        }

        if (options.Count < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Decision requires at least two options.");
        }

        if (options.Any(o => o is null))
        {
            throw new ArgumentException("Options cannot contain null values.", nameof(options));
        }

        var duplicateOptionId = options
            .GroupBy(o => o.Id, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1)?.Key;

        if (duplicateOptionId is not null)
        {
            throw new ArgumentException($"Option IDs must be unique. Duplicate ID: '{duplicateOptionId}'.", nameof(options));
        }

        var canonicalOptions = options.OrderBy(o => o.Id, StringComparer.Ordinal).ToArray();

        var contextBuilder = new LlmContextBuilder();
        context(contextBuilder);
        string canonicalContextJson = contextBuilder.BuildCanonicalJson();

        var resolvedSampling = sampling ?? DefaultSampling;

        var request = new LlmDecisionRequest(
            StableId: stableId,
            Intent: intent,
            Persona: persona,
            CanonicalContextJson: canonicalContextJson,
            Options: canonicalOptions,
            Sampling: resolvedSampling,
            PromptTemplateVersion: LlmDecisionRequest.DefaultPromptTemplateVersion,
            OutputContractVersion: LlmDecisionRequest.DefaultOutputContractVersion);

        return new LlmDecisionStep(request, storeChosenAs, storeRationaleAs, storeResultJsonAs, policy ?? LlmDecisionPolicy.Default);
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

    }

    private sealed record LlmDecisionStep(
        LlmDecisionRequest Request,
        BbKey<string> StoreChosenAs,
        BbKey<string>? StoreRationaleAs,
        BbKey<string>? StoreResultJsonAs,
        LlmDecisionPolicy Policy) : AiStep, IWaitEvent
    {
        private readonly BbKey<bool> _completedKey = new(BuildDecideKey(Request.StableId, "completed"));
        private readonly BbKey<string> _chosenOptionKey = new(BuildDecideKey(Request.StableId, "chosenOptionId"));
        private readonly BbKey<double> _chosenScoreKey = new(BuildDecideKey(Request.StableId, "chosenScore"));
        private readonly BbKey<string> _rationaleKey = new(BuildDecideKey(Request.StableId, "rationale"));
        private readonly BbKey<string> _resultJsonKey = new(BuildDecideKey(Request.StableId, "resultJson"));
        private readonly BbKey<string> _requestHashKey = new(BuildDecideKey(Request.StableId, "requestHash"));
        private readonly BbKey<long> _lastScoredTickKey = new(BuildDecideKey(Request.StableId, "lastScoredTick"));
        private readonly BbKey<long> _committedUntilTickKey = new(BuildDecideKey(Request.StableId, "committedUntilTick"));
        private readonly BbKey<long> _pendingActuationIdKey = new(BuildDecideKey(Request.StableId, "pendingActuationId"));

        public bool TryConsume(AiCtx ctx, ref EventCursor cursor)
        {
            var currentTick = GetCurrentTick(ctx);

            if (TryReuseCommittedChoice(ctx, currentTick, out var reuseReason))
            {
                WriteCommitRationaleAndOutputs(ctx, reuseReason, updateCachedRationale: true);
                return true;
            }

            var pendingIdValue = ctx.Bb.GetOrDefault(_pendingActuationIdKey, 0L);
            if (pendingIdValue == 0)
            {
                var dispatch = ctx.Act.Dispatch(ctx, Request);

                if (!dispatch.Accepted || (dispatch.Completed && !dispatch.Ok))
                {
                    throw new InvalidOperationException(
                        $"Llm.Decide dispatch failed for stableId '{Request.StableId}'. {dispatch.Error ?? "Actuation rejected."}");
                }

                ctx.Bb.Set(_pendingActuationIdKey, dispatch.Id.Value);
                ctx.Bb.Set(_requestHashKey, LlmDecisionRequestHasher.ComputeHash(Request));
                pendingIdValue = dispatch.Id.Value;
            }

            var pendingId = new ActuationId(pendingIdValue);
            if (!ctx.Events.TryConsume(
                    ref cursor,
                    (ActuationCompleted<LlmDecisionResult> e) => e.Id.Equals(pendingId),
                    out var completed))
            {
                return false;
            }

            if (!completed.Ok)
            {
                throw new InvalidOperationException(
                    $"Llm.Decide completion failed for stableId '{Request.StableId}'. {completed.Error ?? "Unknown error."}");
            }

            var result = completed.Payload ?? throw new InvalidOperationException(
                $"Llm.Decide completion failed for stableId '{Request.StableId}'. Missing decision payload.");

            var modelRankOne = result.Scores.SingleOrDefault(s => s.Rank == 1)
                ?? throw new InvalidOperationException(
                    $"Llm.Decide completion failed for stableId '{Request.StableId}'. No Rank=1 score found.");

            var decision = ChooseCommittedOption(ctx, result, modelRankOne);
            var resultJson = BuildDecisionSummaryJson(result, decision, Policy);

            ctx.Bb.Set(_chosenOptionKey, decision.ChosenOptionId);
            ctx.Bb.Set(StoreChosenAs, decision.ChosenOptionId);
            ctx.Bb.Set(_chosenScoreKey, decision.ChosenScore);
            ctx.Bb.Set(_rationaleKey, decision.CommitRationale);
            if (StoreRationaleAs is BbKey<string> rationaleStoreAs)
            {
                ctx.Bb.Set(rationaleStoreAs, decision.CommitRationale);
            }

            ctx.Bb.Set(_resultJsonKey, resultJson);
            if (StoreResultJsonAs is BbKey<string> resultJsonStoreAs)
            {
                ctx.Bb.Set(resultJsonStoreAs, resultJson);
            }

            ctx.Bb.Set(_requestHashKey, result.RequestHash);
            ctx.Bb.Set(_lastScoredTickKey, currentTick);
            ctx.Bb.Set(_committedUntilTickKey, checked(currentTick + Policy.MinCommitTicks));
            ctx.Bb.Set(_completedKey, true);
            ctx.Bb.Set(_pendingActuationIdKey, 0L);
            return true;
        }

        private bool TryReuseCommittedChoice(AiCtx ctx, long currentTick, out string reason)
        {
            reason = string.Empty;
            if (!ctx.Bb.GetOrDefault(_completedKey, false) || !ctx.Bb.TryGet(_chosenOptionKey, out string? cachedChosen))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(cachedChosen) && currentTick < ctx.Bb.GetOrDefault(_committedUntilTickKey, 0L))
            {
                ctx.Bb.Set(StoreChosenAs, cachedChosen);
                reason = $"Reused previous choice within min-commit window (until tick {ctx.Bb.GetOrDefault(_committedUntilTickKey, 0L)}).";
                return true;
            }

            var lastScoredTick = ctx.Bb.GetOrDefault(_lastScoredTickKey, -1L);
            if (lastScoredTick >= 0 && currentTick < checked(lastScoredTick + Policy.RescoreEveryTicks))
            {
                ctx.Bb.Set(StoreChosenAs, cachedChosen ?? string.Empty);
                reason = $"Reused previous choice within rescore cadence window (next rescore at tick {lastScoredTick + Policy.RescoreEveryTicks}).";
                return true;
            }

            return false;
        }

        private void WriteCommitRationaleAndOutputs(AiCtx ctx, string reason, bool updateCachedRationale)
        {
            if (updateCachedRationale)
            {
                ctx.Bb.Set(_rationaleKey, reason);
            }

            if (StoreRationaleAs is BbKey<string> rationaleStoreAs)
            {
                ctx.Bb.Set(rationaleStoreAs, reason);
            }

            if (StoreResultJsonAs is BbKey<string> resultJsonStoreAs)
            {
                if (ctx.Bb.TryGet(_resultJsonKey, out string? resultJson))
                {
                    ctx.Bb.Set(resultJsonStoreAs, resultJson ?? string.Empty);
                }
            }
        }

        private CommitDecision ChooseCommittedOption(AiCtx ctx, LlmDecisionResult result, LlmDecisionOptionScore modelRankOne)
        {
            if (!ctx.Bb.GetOrDefault(_completedKey, false) || !ctx.Bb.TryGet(_chosenOptionKey, out string? previousChosen))
            {
                return new CommitDecision(
                    ChosenOptionId: modelRankOne.OptionId,
                    ChosenScore: modelRankOne.Score,
                    ModelRankOneOptionId: modelRankOne.OptionId,
                    RetainedPreviousChoice: false,
                    RetentionReason: null,
                    CommitRationale: result.Rationale,
                    ModelRationale: result.Rationale);
            }

            var previousId = previousChosen ?? string.Empty;
            if (!Request.Options.Any(o => string.Equals(o.Id, previousId, StringComparison.Ordinal)))
            {
                return new CommitDecision(
                    ChosenOptionId: modelRankOne.OptionId,
                    ChosenScore: modelRankOne.Score,
                    ModelRankOneOptionId: modelRankOne.OptionId,
                    RetainedPreviousChoice: false,
                    RetentionReason: "Previous option removed from current option set.",
                    CommitRationale: "Committed new rank-1 option because previous option was removed.",
                    ModelRationale: result.Rationale);
            }

            var previousScore = result.Scores.SingleOrDefault(s => string.Equals(s.OptionId, previousId, StringComparison.Ordinal));
            if (previousScore is null)
            {
                return new CommitDecision(
                    ChosenOptionId: modelRankOne.OptionId,
                    ChosenScore: modelRankOne.Score,
                    ModelRankOneOptionId: modelRankOne.OptionId,
                    RetainedPreviousChoice: false,
                    RetentionReason: "Previous option missing from decision scores.",
                    CommitRationale: "Committed new rank-1 option because previous option was not scored.",
                    ModelRationale: result.Rationale);
            }

            if (string.Equals(previousId, modelRankOne.OptionId, StringComparison.Ordinal))
            {
                return new CommitDecision(
                    ChosenOptionId: modelRankOne.OptionId,
                    ChosenScore: modelRankOne.Score,
                    ModelRankOneOptionId: modelRankOne.OptionId,
                    RetainedPreviousChoice: false,
                    RetentionReason: null,
                    CommitRationale: result.Rationale,
                    ModelRationale: result.Rationale);
            }

            var switchThreshold = previousScore.Score + Policy.HysteresisMargin;
            if (modelRankOne.Score >= switchThreshold)
            {
                return new CommitDecision(
                    ChosenOptionId: modelRankOne.OptionId,
                    ChosenScore: modelRankOne.Score,
                    ModelRankOneOptionId: modelRankOne.OptionId,
                    RetainedPreviousChoice: false,
                    RetentionReason: "Switched because rank-1 option cleared hysteresis margin.",
                    CommitRationale: $"Switched to '{modelRankOne.OptionId}' because it cleared hysteresis margin over '{previousId}'.",
                    ModelRationale: result.Rationale);
            }

            return new CommitDecision(
                ChosenOptionId: previousId,
                ChosenScore: previousScore.Score,
                ModelRankOneOptionId: modelRankOne.OptionId,
                RetainedPreviousChoice: true,
                RetentionReason: "Rank-1 option did not clear hysteresis margin.",
                CommitRationale: $"Retained '{previousId}' because rank-1 option '{modelRankOne.OptionId}' did not clear hysteresis margin.",
                ModelRationale: result.Rationale);
        }

        private void RestoreOptionalOutputs(AiCtx ctx)
        {
            if (StoreRationaleAs is BbKey<string> rationaleStoreAs && ctx.Bb.TryGet(_rationaleKey, out string? rationale))
            {
                ctx.Bb.Set(rationaleStoreAs, rationale ?? string.Empty);
            }

            if (StoreResultJsonAs is BbKey<string> resultJsonStoreAs && ctx.Bb.TryGet(_resultJsonKey, out string? resultJson))
            {
                ctx.Bb.Set(resultJsonStoreAs, resultJson ?? string.Empty);
            }
        }

        private static long GetCurrentTick(AiCtx ctx)
            => checked((long)Math.Floor(ctx.World.Clock.Time));
    }

    private static string BuildDecisionSummaryJson(LlmDecisionResult result, CommitDecision decision, LlmDecisionPolicy policy)
    {
        var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        writer.WriteString("requestHash", result.RequestHash);

        writer.WriteString("modelRankOneOptionId", decision.ModelRankOneOptionId);
        writer.WriteString("chosenOptionId", decision.ChosenOptionId);
        writer.WriteBoolean("retainedPreviousChoice", decision.RetainedPreviousChoice);
        if (!string.IsNullOrWhiteSpace(decision.RetentionReason))
        {
            writer.WriteString("retentionReason", decision.RetentionReason);
        }

        writer.WritePropertyName("policy");
        writer.WriteStartObject();
        writer.WriteNumber("minCommitTicks", policy.MinCommitTicks);
        writer.WriteNumber("rescoreEveryTicks", policy.RescoreEveryTicks);
        writer.WriteNumber("hysteresisMargin", policy.HysteresisMargin);
        writer.WriteEndObject();

        writer.WriteString("rationale", decision.CommitRationale);
        writer.WriteString("modelRationale", decision.ModelRationale);

        writer.WritePropertyName("scores");
        writer.WriteStartArray();
        foreach (var score in result.Scores.OrderBy(s => s.OptionId, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("optionId", score.OptionId);
            writer.WriteNumber("score", score.Score);
            writer.WriteNumber("rank", score.Rank);
            writer.WriteString("rationale", score.Rationale);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private sealed record CommitDecision(
        string ChosenOptionId,
        double ChosenScore,
        string ModelRankOneOptionId,
        bool RetainedPreviousChoice,
        string? RetentionReason,
        string CommitRationale,
        string ModelRationale);

    private static string BuildStepKey(string stableId, string suffix)
        => $"llm.{SanitizeStableId(stableId)}.{suffix}";

    private static string BuildDecideKey(string stableId, string suffix)
        => $"llm.decide.{SanitizeStableId(stableId)}.{suffix}";

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
