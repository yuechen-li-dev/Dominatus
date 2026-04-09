using Ariadne.OptFlow;
using Ariadne.OptFlow.Commands;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Ariadne.ConsoleApp.Scripts;

public static class RustSimulator
{
    // ---------------------------------------------------------------------
    // Shared/system keys
    // ---------------------------------------------------------------------

    public static readonly BbKey<bool> AdventureComplete = new("System.AdventureComplete");

    // Progress
    public static readonly BbKey<int> Level = new("RustSim.Level");
    public static readonly BbKey<bool> CompletedLevel1 = new("RustSim.CompletedLevel1");

    // Character state
    public static readonly BbKey<int> Confidence = new("RustSim.Confidence");
    public static readonly BbKey<int> Sanity = new("RustSim.Sanity");
    public static readonly BbKey<int> TechDebt = new("RustSim.TechDebt");

    // Level 1-specific flags
    public static readonly BbKey<bool> ReadTheErrorCarefully = new("RustSim.L1.ReadTheErrorCarefully");
    public static readonly BbKey<bool> ClonedEverything = new("RustSim.L1.ClonedEverything");
    public static readonly BbKey<bool> AskedRubberDuck = new("RustSim.L1.AskedRubberDuck");
    public static readonly BbKey<bool> AcceptedOwnershipTruth = new("RustSim.L1.AcceptedOwnershipTruth");
    public static readonly BbKey<bool> AskedVelvet = new("RustSim.L1.AskedVelvet");
    public static readonly BbKey<bool> AskedNimbus = new("RustSim.L1.AskedNimbus");
    public static readonly BbKey<bool> AskedMiniJim = new("RustSim.L1.AskedMiniJim");

    // Menu / branching scratch
    public static readonly BbKey<string> RootChoice = new("RustSim.RootChoice");
    public static readonly BbKey<string> Level1Choice = new("RustSim.Level1Choice");
    public static readonly BbKey<string> EndingChoice = new("RustSim.EndingChoice");

    //Puzzle
    public static readonly BbKey<string> Level1PuzzleAnswer = new("RustSim.L1.PuzzleAnswer");
    public static readonly BbKey<int> Level1PuzzleAttempts = new("RustSim.L1.PuzzleAttempts");

    // ---------------------------------------------------------------------
    // Root / state graph
    // ---------------------------------------------------------------------

    public static IEnumerator<AiStep> Root(AiCtx ctx)
    {
        // Initial seed
        if (ctx.Bb.GetOrDefault(Level, 0) == 0)
        {
            ctx.Bb.Set(Level, 1);
            ctx.Bb.Set(Confidence, 2);
            ctx.Bb.Set(Sanity, 3);
            ctx.Bb.Set(TechDebt, 0);
        }

        yield return Ai.Goto("Intro");

        while (true)
            yield return Ai.Wait(999f);
    }

    public static IEnumerator<AiStep> Intro(AiCtx ctx)
    {
        yield return Diag.Line("2:13 AM. The office is empty except for you, a flickering monitor, and a build that refuses to forgive.", speaker: "Narrator");
        yield return Diag.Line("A red error message glows on the screen like a tiny accusation from a disappointed god.", speaker: "Narrator");
        yield return Diag.Line("Welcome to Rust Simulator.", speaker: "System");
        yield return Ai.Goto("Hub");
    }

    public static IEnumerator<AiStep> Hub(AiCtx ctx)
    {
        while (true)
        {
            var level = ctx.Bb.GetOrDefault(Level, 1);

            var options = new List<DiagChoice>();

            if (level == 1 && !ctx.Bb.GetOrDefault(CompletedLevel1, false))
                options.Add(Diag.Option("l1", "Level 1 - The Borrow Checker Says No"));

            options.Add(Diag.Option("status", "Check your condition"));
            options.Add(Diag.Option("quit", "Abandon your career and leave"));

            yield return Diag.Choose("What now?", options, RootChoice);

            var choice = ctx.Bb.GetOrDefault(RootChoice, "");

            switch (choice)
            {
                case "l1":
                    yield return Ai.Goto("Level1_Intro");
                    yield break;

                case "status":
                    foreach (var step in Diag.SafeInline(ShowStatus(ctx)))
                        yield return step;
                    break;

                case "quit":
                    yield return Ai.Goto("Ending_Quit");
                    yield break;
            }
        }
    }

    // ---------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------

    public static IEnumerable<AiStep> ShowStatus(AiCtx ctx)
    {
        var confidence = ctx.Bb.GetOrDefault(Confidence, 0);
        var sanity = ctx.Bb.GetOrDefault(Sanity, 0);
        var debt = ctx.Bb.GetOrDefault(TechDebt, 0);

        yield return Diag.Line($"Confidence: {confidence}", speaker: "Status");
        yield return Diag.Line($"Sanity: {sanity}", speaker: "Status");
        yield return Diag.Line($"Tech Debt: {debt}", speaker: "Status");
    }

    // ---------------------------------------------------------------------
    // Level 1 - The Borrow Checker Says No
    // ---------------------------------------------------------------------

    public static IEnumerator<AiStep> Level1_Intro(AiCtx ctx)
    {
        yield return Diag.Line("The compiler message is waiting where you left it, patient in the way only a machine can be.", speaker: "Narrator");
        yield return Diag.Line("You borrowed something mutably, then tried to borrow it again. The compiler has noticed. The compiler always notices.", speaker: "Narrator");
        yield return Diag.Line("error[E0499]: cannot borrow `world` as mutable more than once at a time", speaker: "Compiler");
        yield return Ai.Goto("Level1_Menu");
    }

    public static IEnumerator<AiStep> Level1_Menu(AiCtx ctx)
    {
        while (true)
        {
            var options = new List<DiagChoice>();

            if (!ctx.Bb.GetOrDefault(ReadTheErrorCarefully, false))
                options.Add(Diag.Option("read", "Read the error carefully"));

            if (!ctx.Bb.GetOrDefault(AskedRubberDuck, false))
                options.Add(Diag.Option("duck", "Explain the problem to the rubber duck"));

            if (!ctx.Bb.GetOrDefault(ClonedEverything, false))
                options.Add(Diag.Option("clone", "Clone everything and ask questions later"));

            if (!ctx.Bb.GetOrDefault(AcceptedOwnershipTruth, false))
                options.Add(Diag.Option("understand", "Try to understand what ownership is actually complaining about"));

            options.Add(Diag.Option("ai", "Ask AI for help"));
            options.Add(Diag.Option("resolve", "Attempt a fix"));
            options.Add(Diag.Option("flee", "Close the editor and stare into the void"));

            yield return Diag.Choose("Level 1 - The Borrow Checker Says No", options, Level1Choice);

            var choice = ctx.Bb.GetOrDefault(Level1Choice, "");

            switch (choice)
            {
                case "read":
                    yield return Ai.Push("Level1_ReadError");
                    break;

                case "duck":
                    yield return Ai.Push("Level1_AskDuck");
                    break;

                case "clone":
                    yield return Ai.Push("Level1_CloneEverything");
                    break;

                case "understand":
                    yield return Ai.Push("Level1_UnderstandOwnership");
                    break;

                case "ai":
                    yield return Ai.Push("Level1_AIHelp");
                    break;

                case "resolve":
                    yield return Ai.Goto("Level1_Resolve");
                    yield break;

                case "flee":
                    yield return Ai.Goto("Ending_FleeMonitor");
                    yield break;
            }
        }
    }

    public static IEnumerator<AiStep> Level1_ReadError(AiCtx ctx)
    {
        ctx.Bb.Set(ReadTheErrorCarefully, true);
        ctx.Bb.Set(Confidence, ctx.Bb.GetOrDefault(Confidence, 0) + 1);

        yield return Diag.Line("You read the error again. Then a third time. Strangely, it does not become kinder, but it does become more specific.", speaker: "Narrator");
        yield return Diag.Line("The compiler is not saying no because it hates you personally. It is saying no because you promised one mutable truth and attempted to invent another.", speaker: "Compiler");
        yield return Ai.Pop();
    }

    public static IEnumerator<AiStep> Level1_AskDuck(AiCtx ctx)
    {
        ctx.Bb.Set(AskedRubberDuck, true);
        ctx.Bb.Set(Sanity, ctx.Bb.GetOrDefault(Sanity, 0) + 1);

        yield return Diag.Line("You explain the code to the rubber duck on your desk.", speaker: "Narrator");
        yield return Diag.Line("Halfway through, you hear yourself say the phrase 'well obviously that borrow still exists there,' and the duck achieves enlightenment before you do.", speaker: "Narrator");
        yield return Ai.Pop();
    }

    public static IEnumerator<AiStep> Level1_AIHelp(AiCtx ctx)
    {
        while (true)
        {
            var options = new List<DiagChoice>();

            if (!ctx.Bb.GetOrDefault(AskedVelvet, false))
                options.Add(Diag.Option("velvet", "Ask Velvet"));

            if (!ctx.Bb.GetOrDefault(AskedNimbus, false))
                options.Add(Diag.Option("nimbus", "Ask Nimbus"));

            if (!ctx.Bb.GetOrDefault(AskedMiniJim, false))
                options.Add(Diag.Option("minijim", "Ask MiniJim"));

            options.Add(Diag.Option("back", "Never mind"));

            yield return Diag.Choose("Which AI assistant do you consult?", options, Level1Choice);

            var choice = ctx.Bb.GetOrDefault(Level1Choice, "");

            switch (choice)
            {
                case "velvet":
                    yield return Ai.Push("Level1_AskVelvet");
                    break;

                case "nimbus":
                    yield return Ai.Push("Level1_AskNimbus");
                    break;

                case "minijim":
                    yield return Ai.Push("Level1_AskMiniJim");
                    break;

                case "back":
                    yield return Ai.Pop();
                    yield break;
            }
        }
    }

    public static IEnumerator<AiStep> Level1_AskVelvet(AiCtx ctx)
    {
        ctx.Bb.Set(AskedVelvet, true);
        ctx.Bb.Set(Confidence, ctx.Bb.GetOrDefault(Confidence, 0) + 1);

        yield return Diag.Line("You open Velvet, whose interface radiates calm confidence and suspiciously moisturized certainty.", speaker: "Narrator");
        yield return Diag.Line("[Velvet placeholder advice goes here.]", speaker: "Velvet");
        yield return Diag.Line("You feel briefly reassured in a way that does not survive contact with the actual bug.", speaker: "Narrator");

        yield return Ai.Pop();
    }

    public static IEnumerator<AiStep> Level1_AskNimbus(AiCtx ctx)
    {
        ctx.Bb.Set(AskedNimbus, true);
        ctx.Bb.Set(Sanity, ctx.Bb.GetOrDefault(Sanity, 0) + 1);

        yield return Diag.Line("You consult Nimbus, which responds with the grave composure of a machine preparing to explain your own thoughts back to you in numbered sections.", speaker: "Narrator");
        yield return Diag.Line("[Nimbus placeholder advice goes here.]", speaker: "Nimbus");
        yield return Diag.Line("Somewhere in that answer is either wisdom or upholstery. It is too early in the morning to tell which.", speaker: "Narrator");

        yield return Ai.Pop();
    }

    public static IEnumerator<AiStep> Level1_AskMiniJim(AiCtx ctx)
    {
        ctx.Bb.Set(AskedMiniJim, true);
        ctx.Bb.Set(TechDebt, ctx.Bb.GetOrDefault(TechDebt, 0) + 1);

        yield return Diag.Line("You ask MiniJim, which replies instantly, as if speed were a substitute for accuracy and enthusiasm a substitute for thought.", speaker: "Narrator");
        yield return Diag.Line("[MiniJim placeholder advice goes here.]", speaker: "MiniJim");
        yield return Diag.Line("You are now in possession of an answer. Whether it is also help remains a separate theological question.", speaker: "Narrator");

        yield return Ai.Pop();
    }

    public static IEnumerator<AiStep> Level1_CloneEverything(AiCtx ctx)
    {
        ctx.Bb.Set(ClonedEverything, true);
        ctx.Bb.Set(TechDebt, ctx.Bb.GetOrDefault(TechDebt, 0) + 2);
        ctx.Bb.Set(Confidence, ctx.Bb.GetOrDefault(Confidence, 0) + 1);

        yield return Diag.Line("You duplicate data with the frantic confidence of a person outrunning tomorrow.", speaker: "Narrator");
        yield return Diag.Line("The error retreats a few lines. You know this is not victory. The code knows this is not victory. But for one shining second, the build almost flinches.", speaker: "Narrator");
        yield return Ai.Pop();
    }

    public static IEnumerator<AiStep> Level1_UnderstandOwnership(AiCtx ctx)
    {
        ctx.Bb.Set(AcceptedOwnershipTruth, true);
        ctx.Bb.Set(Confidence, ctx.Bb.GetOrDefault(Confidence, 0) + 2);

        yield return Diag.Line("You stop trying to out-argue the type system and ask a more humiliating question: what if it is right?", speaker: "Narrator");
        yield return Diag.Line("A cold, clean understanding arrives. The borrow lives longer than you wanted. The compiler is not blocking progress. It is preserving causality.", speaker: "Narrator");
        yield return Ai.Pop();
    }

    public static IEnumerator<AiStep> Level1_Resolve(AiCtx ctx)
    {
        var read = ctx.Bb.GetOrDefault(ReadTheErrorCarefully, false);
        var duck = ctx.Bb.GetOrDefault(AskedRubberDuck, false);
        var clone = ctx.Bb.GetOrDefault(ClonedEverything, false);
        var understand = ctx.Bb.GetOrDefault(AcceptedOwnershipTruth, false);

        yield return Diag.Line("Fine. No more bluffing. You open the file and stare at the place where the code and your dignity parted ways.", speaker: "Narrator");
        yield return Diag.Line("There is a missing line. Put the right Rust code there and the borrow checker relents. Put the wrong thing there and the night grows teeth.", speaker: "Narrator");

        yield return Diag.Line("Broken snippet:", speaker: "System");
        yield return Diag.Line("let player = world.player_mut();", speaker: "Code");
        yield return Diag.Line("// ???", speaker: "Code");
        yield return Diag.Line("world.spawn_enemy();", speaker: "Code");

        if (read)
            yield return Diag.Line("Hint: the compiler is angry because the mutable borrow of `world` lives too long.", speaker: "Compiler");

        if (duck)
            yield return Diag.Line("The rubber duck, now spiritually superior to you, reminds you that the simplest fix is often to end a borrow before you do the next mutable thing.", speaker: "Narrator");

        if (understand)
            yield return Diag.Line("You already know the shape of the truth: make the first borrow end before the second mutable borrow begins.", speaker: "Narrator");

        yield return Diag.Ask("Type the missing Rust line:", storeAs: Level1PuzzleAnswer);

        var attempts = ctx.Bb.GetOrDefault(Level1PuzzleAttempts, 0) + 1;
        ctx.Bb.Set(Level1PuzzleAttempts, attempts);

        var raw = ctx.Bb.GetOrDefault(Level1PuzzleAnswer, "");
        var answer = NormalizeRustLine(raw);

        if (IsAcceptedLevel1Answer(answer))
        {
            if (understand || (read && duck))
            {
                yield return Diag.Line("You type the line, rerun the build, and feel the error collapse inward like a star finally persuaded to stop arguing with gravity.", speaker: "Narrator");
                yield return Diag.Line("The mutable borrow ends where it should. `world.spawn_enemy()` is free to live its own life. The compiler says nothing, which tonight feels almost tender.", speaker: "Narrator");

                ctx.Bb.Set(CompletedLevel1, true);
                ctx.Bb.Set(Level, 2);

                yield return Ai.Goto("Ending_Level1Success");
                yield break;
            }

            if (clone)
            {
                yield return Diag.Line("Against all moral expectation, the code compiles.", speaker: "Narrator");
                yield return Diag.Line("You solved the immediate problem, but the smell of your earlier clones still hangs over the file like incense at a bad shrine.", speaker: "Narrator");

                ctx.Bb.Set(CompletedLevel1, true);
                ctx.Bb.Set(Level, 2);

                yield return Ai.Goto("Ending_Level1CursedSuccess");
                yield break;
            }

            yield return Diag.Line("The line is correct. The build passes. You do not feel victorious so much as briefly tolerated by the universe.", speaker: "Narrator");
            ctx.Bb.Set(CompletedLevel1, true);
            ctx.Bb.Set(Level, 2);

            yield return Ai.Goto("Ending_Level1Success");
            yield break;
        }

        ctx.Bb.Set(Confidence, Math.Max(0, ctx.Bb.GetOrDefault(Confidence, 0) - 1));

        yield return Diag.Line("The compiler reads your answer and responds with the calm cruelty of something that was never confused in the first place.", speaker: "Compiler");

        if (attempts == 1)
        {
            yield return Diag.Line("That is not the line.", speaker: "Compiler");
            yield return Diag.Line("You have angered the machine, but not beyond forgiveness. Yet.", speaker: "Narrator");
            yield return Ai.Goto("Level1_Menu");
            yield break;
        }

        yield return Diag.Line("Still wrong.", speaker: "Compiler");
        yield return Diag.Line("The bug remains. Worse, it has now seen you flinch.", speaker: "Narrator");
        yield return Ai.Goto("Ending_Level1Failure");
    }

    // ---------------------------------------------------------------------
    // Endings / temporary endpoints
    // ---------------------------------------------------------------------

    public static IEnumerator<AiStep> Ending_Level1Success(AiCtx ctx)
    {
        yield return Diag.Line("Level 1 complete: The Borrow Checker Says No", speaker: "System");
        yield return Diag.Line("You have survived the first chamber.", speaker: "Narrator");
        yield return Diag.Line("More suffering will be patched in later.", speaker: "System");
        ctx.Bb.Set(AdventureComplete, true);
        yield return Ai.Succeed();
    }

    public static IEnumerator<AiStep> Ending_Level1CursedSuccess(AiCtx ctx)
    {
        yield return Diag.Line("Level 1 complete: It Works, Which Is Not The Same As Winning", speaker: "System");
        yield return Diag.Line("The code compiles, but somewhere in the distance a future maintainer begins to cry.", speaker: "Narrator");
        yield return Diag.Line("More suffering will be patched in later.", speaker: "System");
        ctx.Bb.Set(AdventureComplete, true);
        yield return Ai.Succeed();
    }

    public static IEnumerator<AiStep> Ending_Level1Failure(AiCtx ctx)
    {
        yield return Diag.Line("You have not solved the bug. You have merely angered it.", speaker: "Narrator");
        yield return Diag.Line("For tonight, that counts as an ending.", speaker: "Narrator");
        ctx.Bb.Set(AdventureComplete, true);
        yield return Ai.Succeed();
    }

    public static IEnumerator<AiStep> Ending_FleeMonitor(AiCtx ctx)
    {
        yield return Diag.Line("You close the editor and stare into the monitor's black reflection.", speaker: "Narrator");
        yield return Diag.Line("It is still you, but with less confidence and more stack traces.", speaker: "Narrator");
        ctx.Bb.Set(AdventureComplete, true);
        yield return Ai.Succeed();
    }

    public static IEnumerator<AiStep> Ending_Quit(AiCtx ctx)
    {
        yield return Diag.Line("You stand up, leave the office, and allow the bug to become folklore for someone else.", speaker: "Narrator");
        ctx.Bb.Set(AdventureComplete, true);
        yield return Ai.Succeed();
    }

    //Helper Functions
    private static string NormalizeRustLine(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";

        var trimmed = input.Trim();

        // Collapse all whitespace runs to a single space so the player
        // is solving the borrow problem, not fighting spacing trivia.
        var parts = trimmed
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return string.Join(" ", parts);
    }

    private static bool IsAcceptedLevel1Answer(string normalized)
    {
        // Level 1's intended lesson:
        // end the mutable borrow before borrowing world mutably again.
        //
        // We accept a small mercy-set of equivalent answers rather than forcing
        // one exact formatting string.
        return normalized switch
        {
            "drop(player);" => true,
            "std::mem::drop(player);" => true,
            _ => false
        };
    }

    //State Machine Graph Builder
    public static void Register(Dominatus.Core.Hfsm.HfsmGraph graph)
    {
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Root", Node = Root });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Intro", Node = Intro });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Hub", Node = Hub });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Level1_Intro", Node = Level1_Intro });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Level1_Menu", Node = Level1_Menu });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Level1_ReadError", Node = Level1_ReadError });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Level1_AskDuck", Node = Level1_AskDuck });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Level1_AIHelp", Node = Level1_AIHelp });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Level1_AskVelvet", Node = Level1_AskVelvet });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Level1_AskNimbus", Node = Level1_AskNimbus });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Level1_AskMiniJim", Node = Level1_AskMiniJim });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Level1_CloneEverything", Node = Level1_CloneEverything });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Level1_UnderstandOwnership", Node = Level1_UnderstandOwnership });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Level1_Resolve", Node = Level1_Resolve });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Ending_Level1Success", Node = Ending_Level1Success });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Ending_Level1CursedSuccess", Node = Ending_Level1CursedSuccess });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Ending_Level1Failure", Node = Ending_Level1Failure });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Ending_FleeMonitor", Node = Ending_FleeMonitor });
        graph.Add(new Dominatus.Core.Hfsm.HfsmStateDef { Id = "Ending_Quit", Node = Ending_Quit });
    }
}