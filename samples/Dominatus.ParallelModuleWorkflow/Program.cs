namespace Dominatus.ParallelModuleWorkflow;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var result = await ParallelModuleWorkflowDemo.RunAsync(Console.Out);

        Console.WriteLine();
        Console.WriteLine("Summary");
        Console.WriteLine($"Contract: {result.Contract}");
        Console.WriteLine($"Max observed concurrency: {result.MaxObservedConcurrency}");
        Console.WriteLine($"Used Llm.Call: {result.UsedLlmCall}");
        Console.WriteLine("Module outputs:");
        foreach (var module in result.ModuleResults)
        {
            Console.WriteLine($"- {module.Module}: {module.Output}");
        }
    }
}
