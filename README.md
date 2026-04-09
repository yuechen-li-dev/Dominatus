# Dominatus

Dominatus is a .NET 8 agent runtime kernel for deterministic, stateful AI.

It combines hierarchical finite state machines with utility-based decision-making to execute agentic AI behavior as C# iterator-driven step streams in a way that is structured, inspectable, and persistable.

Dominatus is **not** a wrapper around LLM calls, prompt orchestration, or Python automation tooling. It is a standalone runtime kernel for agents with memory, structured control flow, commands, and save/restore semantics.

It is **not** a dialogue framework, though dialogue systems can be built on top of it, as Ariadne does.

It is **not** a behavior tree library, though behavior-tree-like patterns can be expressed naturally within its control-flow model.

And it does **not** depend on LLMs, even though LLM integration may be added later.

At its core, Dominatus is a general-purpose runtime for any system that needs agents with memory, commands, explicit control flow, and save/restore support — from video games and simulations to industrial and control-oriented software.

Dominatus was heavily inspired by the AI tactics system used in Bioware's Dragon Age: Origins.

## Getting Started

- [Architecture](https://github.com/yuechen-li-dev/Dominatus/blob/master/docs/ARCHITECTURE.md): A short introductory overview to Dominatus' architecture and systems.
- [Authoring Guide](https://github.com/yuechen-li-dev/Dominatus/blob/master/docs/AUTHORING_GUIDE.md): A practical authoring guide on:
  - How to write a node.
  - What steps are available.
  - How to read and write the blackboard.
  - How to register states.
  - Etc.

## Sample Projects

1. [Ariadne.ConsoleApp](https://github.com/yuechen-li-dev/Dominatus/tree/master/src/Ariadne.Console): A small text adventure runner, with 2 builtin text adventures included.
2. [Dominatus.FishTank](https://github.com/yuechen-li-dev/Dominatus/tree/master/samples/Dominatus.FishTank): A fish tank simulator built with MonoGame, utilizing utility AI to simulate fish behavior.

## License

[MIT](https://github.com/yuechen-li-dev/Dominatus/blob/master/LICENSE.txt)
