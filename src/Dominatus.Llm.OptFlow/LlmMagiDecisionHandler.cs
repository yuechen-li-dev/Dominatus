using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public sealed class LlmMagiDecisionHandler : AsyncActuationHandler<LlmMagiRequest>
{
    private readonly ILlmDecisionClient _advocateAClient;
    private readonly ILlmDecisionClient _advocateBClient;
    private readonly ILlmMagiJudgeClient _judgeClient;
    private readonly ILlmMagiCassette _cassette;
    private readonly LlmCassetteMode _mode;
    private readonly object _cassetteGate = new();

    public LlmMagiDecisionHandler(
        ILlmDecisionClient advocateAClient,
        ILlmDecisionClient advocateBClient,
        ILlmMagiJudgeClient judgeClient,
        ILlmMagiCassette cassette,
        LlmCassetteMode mode)
    {
        _advocateAClient = advocateAClient ?? throw new ArgumentNullException(nameof(advocateAClient));
        _advocateBClient = advocateBClient ?? throw new ArgumentNullException(nameof(advocateBClient));
        _judgeClient = judgeClient ?? throw new ArgumentNullException(nameof(judgeClient));
        _cassette = cassette ?? throw new ArgumentNullException(nameof(cassette));
        _mode = mode;
    }

    public override async ValueTask<ActuatorHost.HandlerResult> HandleAsync(AsyncActuationContext context, LlmMagiRequest cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var requestHash = LlmMagiRequestHasher.ComputeHash(cmd);

        try
        {
            var result = await ResolveAsync(cmd, requestHash, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return ActuatorHost.HandlerResult.CompletedWithPayload(result);
        }
        catch (Exception ex)
        {
            return new ActuatorHost.HandlerResult(
                Accepted: true,
                Completed: true,
                Ok: false,
                Error: BuildFailureMessage(_mode, cmd.StableId, requestHash, ex.Message));
        }
    }

    private async ValueTask<LlmMagiDecisionResult> ResolveAsync(LlmMagiRequest request, string requestHash, CancellationToken cancellationToken)
    {
        if (_mode is LlmCassetteMode.Replay or LlmCassetteMode.Strict)
        {
            if (!TryGet(requestHash, out var replayResult))
            {
                throw new InvalidOperationException("Cassette entry not found.");
            }

            LlmMagiResultValidator.ValidateDecisionResultAgainstRequest(request, requestHash, replayResult);
            return replayResult;
        }

        if (_mode is LlmCassetteMode.Record && TryGet(requestHash, out var recorded))
        {
            LlmMagiResultValidator.ValidateDecisionResultAgainstRequest(request, requestHash, recorded);
            return recorded;
        }

        if (_mode is not (LlmCassetteMode.Live or LlmCassetteMode.Record))
            throw new InvalidOperationException($"Unsupported cassette mode '{_mode}'.");

        var advocateARequest = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var advocateAHash = LlmDecisionRequestHasher.ComputeHash(advocateARequest);
        var advocateBRequest = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);
        var advocateBHash = LlmDecisionRequestHasher.ComputeHash(advocateBRequest);
        var advocateATask = ScoreAdvocateAsync(_advocateAClient, advocateARequest, advocateAHash, cancellationToken);
        var advocateBTask = ScoreAdvocateAsync(_advocateBClient, advocateBRequest, advocateBHash, cancellationToken);

        await WaitForAdvocatesAsync(advocateATask, advocateBTask).ConfigureAwait(false);

        var advocateAResult = await advocateATask.ConfigureAwait(false);
        var advocateBResult = await advocateBTask.ConfigureAwait(false);

        if (advocateAResult is null)
        {
            throw new InvalidOperationException("Magi advocate A provider returned null result.");
        }

        if (advocateBResult is null)
        {
            throw new InvalidOperationException("Magi advocate B provider returned null result.");
        }

        LlmDecisionResultValidator.ValidateAgainstRequest(advocateARequest, advocateAHash, advocateAResult);
        LlmDecisionResultValidator.ValidateAgainstRequest(advocateBRequest, advocateBHash, advocateBResult);

        cancellationToken.ThrowIfCancellationRequested();
        var judgment = await _judgeClient.JudgeAsync(request, requestHash, advocateAResult, advocateBResult, cancellationToken)
            .ConfigureAwait(false);

        if (judgment is null)
        {
            throw new InvalidOperationException("Magi judge provider returned null judgment.");
        }

        LlmMagiResultValidator.ValidateJudgmentAgainstRequest(request, judgment);

        var result = new LlmMagiDecisionResult(
            RequestHash: requestHash,
            AdvocateA: request.AdvocateA,
            AdvocateB: request.AdvocateB,
            Judge: request.Judge,
            AdvocateAResult: advocateAResult,
            AdvocateBResult: advocateBResult,
            Judgment: judgment);

        LlmMagiResultValidator.ValidateDecisionResultAgainstRequest(request, requestHash, result);

        if (_mode is LlmCassetteMode.Record)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_cassetteGate) _cassette.Put(requestHash, request, result);
        }

        return result;
    }

    private bool TryGet(string hash, out LlmMagiDecisionResult result)
    { lock (_cassetteGate) return _cassette.TryGet(hash, out result); }

    private static string BuildFailureMessage(LlmCassetteMode mode, string stableId, string requestHash, string reason)
        => $"LlmMagi decision failed (Mode={mode}, StableId={stableId}, RequestHash={requestHash}). {reason}";

    // Wrap synchronous provider throws into an owned task so the other advocate is still observed.
    private static async Task<LlmDecisionResult> ScoreAdvocateAsync(ILlmDecisionClient client,
        LlmDecisionRequest request, string hash, CancellationToken cancellationToken)
        => await client.ScoreOptionsAsync(request, hash, cancellationToken).ConfigureAwait(false);

    private static async Task WaitForAdvocatesAsync(Task<LlmDecisionResult> advocateATask, Task<LlmDecisionResult> advocateBTask)
    {
        try
        {
            await Task.WhenAll(advocateATask, advocateBTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failed = new List<string>(2);

            if (advocateATask.IsFaulted)
            {
                failed.Add("advocateA");
            }

            if (advocateBTask.IsFaulted)
            {
                failed.Add("advocateB");
            }

            if (failed.Count is 0)
            {
                failed.Add("unknown");
            }

            throw new InvalidOperationException(
                $"Magi advocate request failed ({string.Join(", ", failed)}).",
                ex);
        }
    }
}
