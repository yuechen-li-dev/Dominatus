namespace Dominatus.SemanticKernelGraphAssistant;

public static class Program
{
    public static void Main()
    {
        GraphAssistantDemo.Run(approvalGranted: false, output: Console.Out);
        Console.WriteLine();
        GraphAssistantDemo.Run(approvalGranted: true, output: Console.Out);
    }
}
