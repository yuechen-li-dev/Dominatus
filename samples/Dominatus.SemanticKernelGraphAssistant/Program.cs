namespace Dominatus.SemanticKernelGraphAssistant;

public static class Program
{
    public static void Main()
    {
        GraphAssistantDemo.Run(approvalGranted: false, scenario: GraphAssistantScenario.UrgentReply, output: Console.Out);
        GraphAssistantDemo.Run(approvalGranted: true, scenario: GraphAssistantScenario.UrgentReply, output: Console.Out);
        Console.WriteLine();
        GraphAssistantDemo.Run(approvalGranted: false, scenario: GraphAssistantScenario.SchedulingRequest, output: Console.Out);
        GraphAssistantDemo.Run(approvalGranted: true, scenario: GraphAssistantScenario.SchedulingRequest, output: Console.Out);
    }
}
