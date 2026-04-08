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

    // Menu / branching scratch
    public static readonly BbKey<string> RootChoice = new("RustSim.RootChoice");
    public static readonly BbKey<string> Level1Choice = new("RustSim.Level1Choice");
    public static readonly BbKey<string> EndingChoice = new("RustSim.EndingChoice");

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
                    foreach (var step in ShowStatus(ctx))
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
        yield return Ai.Pop();
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

        if (understand || (read && duck))
        {
            yield return Diag.Line("You refactor the scope. The mutable borrow ends where it should. The code compiles with the weary dignity of a problem finally named correctly.", speaker: "Narrator");
            yield return Diag.Line("The compiler says nothing. Which, tonight, feels like respect.", speaker: "Narrator");

            ctx.Bb.Set(CompletedLevel1, true);
            ctx.Bb.Set(Level, 2);

            yield return Ai.Goto("Ending_Level1Success");
            yield break;
        }

        if (clone)
        {
            yield return Diag.Line("Technically, it works.", speaker: "Narrator");
            yield return Diag.Line("You stare at the cloned values spreading through the code like emergency scaffolding that forgot to leave after the building was done.", speaker: "Narrator");

            ctx.Bb.Set(CompletedLevel1, true);
            ctx.Bb.Set(Level, 2);

            yield return Ai.Goto("Ending_Level1CursedSuccess");
            yield break;
        }

        yield return Diag.Line("You make a change quickly, confidently, and wrong.", speaker: "Narrator");
        yield return Diag.Line("The original borrow error vanishes, only to be replaced by a newer, more intimate one. The compiler has stopped speaking in objections and started speaking in lessons.", speaker: "Compiler");
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
}