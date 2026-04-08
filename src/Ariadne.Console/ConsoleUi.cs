using System.Text;

namespace Ariadne.ConsoleApp;

public sealed class ConsoleUi
{
    public void Clear()
    {
        Console.Clear();
    }

    public void PrintBanner(string title, string? subtitle = null)
    {
        Console.Clear();
        Console.WriteLine(title);
        Console.WriteLine(new string('=', title.Length));

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            Console.WriteLine(subtitle);
            Console.WriteLine();
        }
    }

    public void PrintLine(string? speaker, string text)
    {
        if (!string.IsNullOrWhiteSpace(speaker))
            Console.WriteLine($"{speaker}: {text}");
        else
            Console.WriteLine(text);
    }

    public void PrintInfo(string text)
    {
        Console.WriteLine(text);
    }

    public void PrintBlank()
    {
        Console.WriteLine();
    }

    public void WaitAdvance()
    {
        Console.WriteLine();
        Console.Write("[Enter] ");
        Console.ReadLine();
    }

    public void WaitForMenuReturn(string? message = null)
    {
        Console.WriteLine();
        Console.Write(message ?? "Press Enter to return to menu...");
        Console.ReadLine();
    }

    public string Ask(string prompt)
    {
        Console.Write($"{prompt} ");
        return Console.ReadLine() ?? "";
    }

    public string Choose(string prompt, IReadOnlyList<(string Key, string Text)> options)
    {
        Console.WriteLine(prompt);
        for (int i = 0; i < options.Count; i++)
            Console.WriteLine($"  [{options[i].Key}] {options[i].Text}");

        while (true)
        {
            Console.Write("> ");
            var input = (Console.ReadLine() ?? "").Trim();
            if (input.Length == 0) continue;

            for (int i = 0; i < options.Count; i++)
                if (string.Equals(options[i].Key, input, StringComparison.OrdinalIgnoreCase))
                    return options[i].Key;

            Console.WriteLine("Invalid choice. Try again.");
        }
    }

    public int ChooseMenu(string prompt, IReadOnlyList<AdventureDefinition> adventures, bool includeQuit = true)
    {
        Console.WriteLine(prompt);
        Console.WriteLine();

        for (int i = 0; i < adventures.Count; i++)
            Console.WriteLine($"  [{i + 1}] {adventures[i].Title} - {adventures[i].Description}");

        if (includeQuit)
            Console.WriteLine("  [Q] Quit");

        while (true)
        {
            Console.Write("> ");
            var input = (Console.ReadLine() ?? "").Trim();

            if (includeQuit && string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
                return -1;

            if (int.TryParse(input, out var n))
            {
                var idx = n - 1;
                if (idx >= 0 && idx < adventures.Count)
                    return idx;
            }

            Console.WriteLine("Invalid selection. Try again.");
        }
    }
}