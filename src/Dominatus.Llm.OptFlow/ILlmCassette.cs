namespace Dominatus.Llm.OptFlow;

public interface ILlmCassette
{
    bool TryGet(string requestHash, out LlmTextResult result);

    void Put(
        string requestHash,
        LlmTextRequest request,
        LlmTextResult result);
}
