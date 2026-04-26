using Dominatus.Core.Runtime;

namespace Dominatus.Llm.OptFlow;

public sealed class LlmMagiDecisionHandler : IActuationHandler<LlmMagiRequest>
{
    private readonly ILlmDecisionClient _advocateAClient;
    private readonly ILlmDecisionClient _advocateBClient;
    private readonly ILlmMagiJudgeClient _judgeClient;
    private readonly ILlmMagiCassette _cassette;
    private readonly LlmCassetteMode _mode;

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

    public ActuatorHost.HandlerResult Handle(ActuatorHost host, AiCtx ctx, ActuationId id, LlmMagiRequest cmd)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(cmd);

        var requestHash = LlmMagiRequestHasher.ComputeHash(cmd);

        try
        {
            var result = Resolve(cmd, requestHash, ctx.Cancel);
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

    private LlmMagiDecisionResult Resolve(LlmMagiRequest request, string requestHash, CancellationToken cancellationToken)
    {
        if (_mode is LlmCassetteMode.Replay or LlmCassetteMode.Strict)
        {
            if (!_cassette.TryGet(requestHash, out var replayResult))
            {
                throw new InvalidOperationException("Cassette entry not found.");
            }

            LlmMagiResultValidator.ValidateDecisionResultAgainstRequest(request, requestHash, replayResult);
            return replayResult;
        }

        if (_mode is LlmCassetteMode.Record && _cassette.TryGet(requestHash, out var recorded))
        {
            LlmMagiResultValidator.ValidateDecisionResultAgainstRequest(request, requestHash, recorded);
            return recorded;
        }

        var advocateARequest = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateA);
        var advocateAHash = LlmDecisionRequestHasher.ComputeHash(advocateARequest);
        var advocateBRequest = LlmMagiResultValidator.BuildAdvocateRequest(request, request.AdvocateB);
        var advocateBHash = LlmDecisionRequestHasher.ComputeHash(advocateBRequest);
        var advocateATask = _advocateAClient.ScoreOptionsAsync(advocateARequest, advocateAHash, cancellationToken);
        var advocateBTask = _advocateBClient.ScoreOptionsAsync(advocateBRequest, advocateBHash, cancellationToken);

        WaitForAdvocates(advocateATask, advocateBTask);

        var advocateAResult = advocateATask.GetAwaiter().GetResult();
        var advocateBResult = advocateBTask.GetAwaiter().GetResult();

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

        var judgment = _judgeClient.JudgeAsync(request, requestHash, advocateAResult, advocateBResult, cancellationToken)
            .GetAwaiter()
            .GetResult();

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
            _cassette.Put(requestHash, request, result);
        }

        return result;
    }

    private static string BuildFailureMessage(LlmCassetteMode mode, string stableId, string requestHash, string reason)
        => $"LlmMagi decision failed (Mode={mode}, StableId={stableId}, RequestHash={requestHash}). {reason}";

    private static void WaitForAdvocates(Task<LlmDecisionResult> advocateATask, Task<LlmDecisionResult> advocateBTask)
    {
        try
        {
            Task.WhenAll(advocateATask, advocateBTask).GetAwaiter().GetResult();
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
