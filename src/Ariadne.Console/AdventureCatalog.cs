using Ariadne.ConsoleApp.Scripts;

namespace Ariadne.ConsoleApp;

public static class AdventureCatalog
{
    private static readonly AdventureDefinition[] _all =
    [
        new(
            Id: "demo",
            Title: "Demo Dialogue",
            Description: "A tiny Ariadne conversation demo.",
            Root: DemoDialogue.Root)
    ];

    public static IReadOnlyList<AdventureDefinition> All => _all;
}