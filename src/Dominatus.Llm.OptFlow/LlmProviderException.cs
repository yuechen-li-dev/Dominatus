namespace Dominatus.Llm.OptFlow;

public class LlmProviderException : Exception
{
    public bool IsFallbackEligible { get; }

    public LlmProviderException(string message, bool isFallbackEligible, Exception? innerException = null)
        : base(message, innerException)
    {
        IsFallbackEligible = isFallbackEligible;
    }
}

public sealed class LlmProviderUnavailableException : LlmProviderException
{
    public LlmProviderUnavailableException(string message, Exception? innerException = null)
        : base(message, isFallbackEligible: true, innerException)
    {
    }
}

public sealed class LlmProviderRateLimitedException : LlmProviderException
{
    public LlmProviderRateLimitedException(string message, Exception? innerException = null)
        : base(message, isFallbackEligible: true, innerException)
    {
    }
}

public sealed class LlmProviderTransientException : LlmProviderException
{
    public LlmProviderTransientException(string message, Exception? innerException = null)
        : base(message, isFallbackEligible: true, innerException)
    {
    }
}
