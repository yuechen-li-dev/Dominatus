using Dominatus.TinyTown;

var result = TinyTownDemo.Run(100, Console.Out);

Console.WriteLine();
Console.WriteLine($"Ticks run: {result.TicksRun}");
Console.WriteLine("Final townies:");
foreach (var t in result.FinalTownies)
{
    Console.WriteLine($"- {t.Name} at {t.Location}: {t.CurrentAction} hunger={t.Hunger:0.00} energy={t.Energy:0.00} social={t.Social:0.00} fun={t.Fun:0.00} hygiene={t.Hygiene:0.00} bladder={t.Bladder:0.00}");
}

Console.WriteLine("Event highlights:");
foreach (var line in result.EventLog.Take(20)) Console.WriteLine($"- {line}");
Console.WriteLine("Dialogue lines:");
foreach (var line in result.DialogueLines) Console.WriteLine($"- {line}");
Console.WriteLine($"LLM call count: {result.LlmCallCount}");
Console.WriteLine("Note: normal utility actions (eat, sleep, work, bathroom, shower, fun, idle) do not call LLMs; Llm.Call is reserved for dialogue flavor.");
