using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public sealed class LlmDecisionScoringHandler : IActuationHandler<LlmDecisionRequest>
{
    private readonly ILlmDecisionClient _client;
    private readonly ILlmDecisionCassette _cassette;
    private readonly LlmCassetteMode _mode;

    public LlmDecisionScoringHandler(
        ILlmDecisionClient client,
        ILlmDecisionCassette cassette,
        LlmCassetteMode mode)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _cassette = cassette ?? throw new ArgumentNullException(nameof(cassette));
        _mode = mode;
    }

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, LlmDecisionRequest cmd)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(cmd);

        string requestHash = LlmDecisionRequestHasher.ComputeHash(cmd);

        try
        {
            var result = Resolve(cmd, requestHash, ctx.Cancel);
            return ActuatorHost.HandlerResult.CompletedWithPayload(result);
        }
        catch (Exception ex)
        {
            var error = BuildFailureMessage(_mode, cmd.StableId, requestHash, ex.Message);
            return new ActuatorHost.HandlerResult(
                Accepted: true,
                Completed: true,
                Ok: false,
                Error: error);
        }
    }

    private LlmDecisionResult Resolve(LlmDecisionRequest request, string requestHash, CancellationToken cancellationToken)
    {
        if (_mode is LlmCassetteMode.Replay or LlmCassetteMode.Strict)
        {
            if (!_cassette.TryGet(requestHash, out var replayResult))
            {
                throw new InvalidOperationException("Cassette entry not found.");
            }

            LlmDecisionResultValidator.ValidateAgainstRequest(request, requestHash, replayResult);
            return replayResult;
        }

        if (_mode is LlmCassetteMode.Record && _cassette.TryGet(requestHash, out var recordedResult))
        {
            LlmDecisionResultValidator.ValidateAgainstRequest(request, requestHash, recordedResult);
            return recordedResult;
        }

        if (_mode is LlmCassetteMode.Live or LlmCassetteMode.Record)
        {
            var providerResult = _client
                .ScoreOptionsAsync(request, requestHash, cancellationToken)
                .GetAwaiter()
                .GetResult();

            if (providerResult is null)
            {
                throw new InvalidOperationException("LLM decision provider returned null result.");
            }

            LlmDecisionResultValidator.ValidateAgainstRequest(request, requestHash, providerResult);

            if (_mode is LlmCassetteMode.Record)
            {
                _cassette.Put(requestHash, request, providerResult);
            }

            return providerResult;
        }

        throw new InvalidOperationException($"Unsupported cassette mode '{_mode}'.");
    }

    private static string BuildFailureMessage(LlmCassetteMode mode, string stableId, string requestHash, string reason)
        => $"LlmDecision scoring failed (Mode={mode}, StableId={stableId}, RequestHash={requestHash}). {reason}";
}
