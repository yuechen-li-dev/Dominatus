using System.Text;

namespace Ariadne.ConsoleApp;

public sealed class ConsoleUi
{
    public void PrintLine(string? speaker, string text)
    {
        if (!string.IsNullOrWhiteSpace(speaker))
            System.Console.WriteLine($"{speaker}: {text}");
        else
            System.Console.WriteLine(text);
    }

    public void WaitAdvance()
    {
        System.Console.WriteLine();
        System.Console.Write("[Enter] ");
        System.Console.ReadLine();
    }

    public string Ask(string prompt)
    {
        System.Console.Write($"{prompt} ");
        return System.Console.ReadLine() ?? "";
    }

    public string Choose(string prompt, IReadOnlyList<(string Key, string Text)> options)
    {
        System.Console.WriteLine(prompt);
        for (int i = 0; i < options.Count; i++)
            System.Console.WriteLine($"  [{options[i].Key}] {options[i].Text}");

        while (true)
        {
            System.Console.Write("> ");
            var input = (System.Console.ReadLine() ?? "").Trim();
            if (input.Length == 0) continue;

            for (int i = 0; i < options.Count; i++)
                if (string.Equals(options[i].Key, input, StringComparison.OrdinalIgnoreCase))
                    return options[i].Key;

            System.Console.WriteLine("Invalid choice. Try again.");
        }
    }
}