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
            Flow: DemoDialogue.Definition),

        new(
            Id: "thread_of_night",
            Title: "Ariadne: Thread of Night",
            Description: "A mythic chamber drama set on the night before the labyrinth.",
            Flow: AriadneThreadOfNight.Definition),

        new(
            Id: "rust_simulator",
            Title: "Rust Simulator",
            Description: "A black-comedy descent through compile-time suffering.",
            Flow: RustSimulator.Definition)
    ];

    public static IReadOnlyList<AdventureDefinition> All => _all;
}
