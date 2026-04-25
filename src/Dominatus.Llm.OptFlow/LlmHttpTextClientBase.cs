using System.Text.Json;

namespace Dominatus.Llm.OptFlow;

public abstract class LlmHttpTextClientBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    protected LlmHttpTextClientBase(LlmHttpProviderOptions options, ILlmHttpTransport transport)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    protected LlmHttpProviderOptions Options { get; }

    protected ILlmHttpTransport Transport { get; }

    protected static JsonSerializerOptions SerializerOptions => JsonOptions;

    protected string BuildContext(string stableId, string requestHash)
        => $"provider='{Options.Provider}', model='{Options.Model}', stableId='{stableId}', requestHash='{requestHash}'";

    protected static string Redact(string input, string secret)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(secret))
        {
            return input;
        }

        return input.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
    }
}
