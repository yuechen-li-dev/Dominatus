namespace Dominatus.Llm.OptFlow;

public interface ILlmMagiCassette
{
    bool TryGet(string requestHash, out LlmMagiDecisionResult result);

    void Put(
        string requestHash,
        LlmMagiRequest request,
        LlmMagiDecisionResult result);
}
