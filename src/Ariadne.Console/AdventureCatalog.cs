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
            RegisterStates: DemoDialogue.Register),

        new(
            Id: "thread_of_night",
            Title: "Ariadne: Thread of Night",
            Description: "A mythic chamber drama set on the night before the labyrinth.",
            RegisterStates: AriadneThreadOfNight.Register),

        new(
            Id: "rust_simulator",
            Title: "Rust Simulator",
            Description: "A black-comedy descent through compile-time suffering.",
            RegisterStates: RustSimulator.Register)
    ];

    public static IReadOnlyList<AdventureDefinition> All => _all;
}