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
            Root: DemoDialogue.Root),

        new(
            Id: "thread_of_night",
            Title: "Ariadne: Thread of Night",
            Description: "A mythic chamber drama set on the night before the labyrinth.",
            Root: AriadneThreadOfNight.Root)
    ];

    public static IReadOnlyList<AdventureDefinition> All => _all;
}