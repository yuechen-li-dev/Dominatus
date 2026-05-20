using Dominatus.SemanticKernelOrchestration;

var trace = args.Contains("--trace", StringComparer.OrdinalIgnoreCase);
SemanticKernelOrchestrationDemo.Run(Console.Out, trace);
