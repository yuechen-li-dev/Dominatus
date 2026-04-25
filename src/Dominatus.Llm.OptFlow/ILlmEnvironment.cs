namespace Dominatus.Llm.OptFlow;

public interface ILlmEnvironment
{
    string? GetEnvironmentVariable(string name);
}

public sealed class ProcessLlmEnvironment : ILlmEnvironment
{
    public static readonly ProcessLlmEnvironment Instance = new();

    private ProcessLlmEnvironment()
    {
    }

    public string? GetEnvironmentVariable(string name)
        => Environment.GetEnvironmentVariable(name);
}
