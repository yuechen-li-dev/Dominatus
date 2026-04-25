namespace Dominatus.Llm.OptFlow;

public sealed record LlmHttpProviderOptions
{
    public string Provider { get; }
    public string Model { get; }
    public Uri Endpoint { get; }
    public string ApiKey { get; }
    public TimeSpan? Timeout { get; }

    public LlmHttpProviderOptions(
        string Provider,
        string Model,
        Uri Endpoint,
        string ApiKey,
        TimeSpan? Timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(Model);
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(ApiKey);

        if (!Endpoint.IsAbsoluteUri)
        {
            throw new ArgumentException("Endpoint must be an absolute URI.", nameof(Endpoint));
        }

        if (Timeout is not null && Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout must be greater than zero when provided.");
        }

        this.Provider = Provider;
        this.Model = Model;
        this.Endpoint = Endpoint;
        this.ApiKey = ApiKey;
        this.Timeout = Timeout;
    }
}
