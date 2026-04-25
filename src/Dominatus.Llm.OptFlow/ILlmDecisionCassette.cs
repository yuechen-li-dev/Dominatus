namespace Dominatus.Llm.OptFlow;

public interface ILlmDecisionCassette
{
    bool TryGet(string requestHash, out LlmDecisionResult result);

    void Put(
        string requestHash,
        LlmDecisionRequest request,
        LlmDecisionResult result);
}
