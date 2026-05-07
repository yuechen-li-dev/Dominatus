namespace Dominatus.Llm.OptFlow;

public sealed record LlmMagiDecisionResult
{
    public string RequestHash { get; }
    public LlmMagiParticipant AdvocateA { get; }
    public LlmMagiParticipant AdvocateB { get; }
    public LlmMagiParticipant Judge { get; }
    public LlmDecisionResult AdvocateAResult { get; }
    public LlmDecisionResult AdvocateBResult { get; }
    public LlmMagiJudgment Judgment { get; }
    public LlmDecisionOutcome Outcome { get; }
    public LlmDecisionRefusal? Refusal { get; }

    public LlmMagiDecisionResult(
        string RequestHash,
        LlmMagiParticipant AdvocateA,
        LlmMagiParticipant AdvocateB,
        LlmMagiParticipant Judge,
        LlmDecisionResult AdvocateAResult,
        LlmDecisionResult AdvocateBResult,
        LlmMagiJudgment Judgment,
        LlmDecisionOutcome Outcome = LlmDecisionOutcome.Chosen,
        LlmDecisionRefusal? Refusal = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RequestHash);
        ArgumentNullException.ThrowIfNull(AdvocateA);
        ArgumentNullException.ThrowIfNull(AdvocateB);
        ArgumentNullException.ThrowIfNull(Judge);
        ArgumentNullException.ThrowIfNull(AdvocateAResult);
        ArgumentNullException.ThrowIfNull(AdvocateBResult);
        ArgumentNullException.ThrowIfNull(Judgment);

        this.RequestHash = RequestHash;
        this.AdvocateA = AdvocateA;
        this.AdvocateB = AdvocateB;
        this.Judge = Judge;
        this.AdvocateAResult = AdvocateAResult;
        this.AdvocateBResult = AdvocateBResult;
        this.Judgment = Judgment;
        this.Outcome = Outcome;
        this.Refusal = Refusal;
    }
}
