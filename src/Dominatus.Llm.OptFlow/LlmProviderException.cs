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
    public TimeSpan? RetryAfter { get; }

    public LlmProviderRateLimitedException(string message, Exception? innerException = null)
        : this(message, retryAfter: null, innerException)
    {
    }

    public LlmProviderRateLimitedException(string message, TimeSpan? retryAfter, Exception? innerException = null)
        : base(message, isFallbackEligible: true, innerException)
    {
        RetryAfter = retryAfter;
    }
}

public sealed class LlmProviderTransientException : LlmProviderException
{
    public LlmProviderTransientException(string message, Exception? innerException = null)
        : base(message, isFallbackEligible: true, innerException)
    {
    }
}
