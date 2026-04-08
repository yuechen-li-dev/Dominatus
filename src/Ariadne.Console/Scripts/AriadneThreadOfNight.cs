using Ariadne.OptFlow;
using Ariadne.OptFlow.Commands;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Ariadne.ConsoleApp.Scripts;

public static class AriadneThreadOfNight
{
    // ---------------------------------------------------------------------
    // Blackboard keys
    // ---------------------------------------------------------------------

    public static readonly BbKey<bool> AdventureComplete = new("System.AdventureComplete");

    public static readonly BbKey<bool> TrustsTheseus = new("Ariadne.TrustsTheseus");
    public static readonly BbKey<bool> PitiesMinotaur = new("Ariadne.PitiesMinotaur");
    public static readonly BbKey<bool> DefiesMinos = new("Ariadne.DefiesMinos");
    public static readonly BbKey<bool> WantsEscape = new("Ariadne.WantsEscape");

    public static readonly BbKey<bool> ThreadPrepared = new("Ariadne.ThreadPrepared");
    public static readonly BbKey<bool> KnifeTaken = new("Ariadne.KnifeTaken");
    public static readonly BbKey<bool> ShrineVisited = new("Ariadne.ShrineVisited");
    public static readonly BbKey<bool> AdmittedFear = new("Ariadne.AdmittedFear");

    public static readonly BbKey<bool> PromisedMercy = new("Ariadne.PromisedMercy");
    public static readonly BbKey<bool> TheseusFailedTest = new("Ariadne.TheseusFailedTest");

    public static readonly BbKey<bool> SeenThread = new("Ariadne.SeenThread");
    public static readonly BbKey<bool> SeenKnife = new("Ariadne.SeenKnife");
    public static readonly BbKey<bool> SeenTablets = new("Ariadne.SeenTablets");
    public static readonly BbKey<bool> SeenShrine = new("Ariadne.SeenShrine");

    public static readonly BbKey<bool> AskedWhy = new("Ariadne.AskedWhy");
    public static readonly BbKey<bool> AskedFear = new("Ariadne.AskedFear");
    public static readonly BbKey<bool> AskedMonster = new("Ariadne.AskedMonster");
    public static readonly BbKey<bool> AskedPromise = new("Ariadne.AskedPromise");

    public static readonly BbKey<string> ChamberChoice = new("Ariadne.ChamberChoice");
    public static readonly BbKey<string> TheseusChoice = new("Ariadne.TheseusChoice");
    public static readonly BbKey<string> FinalChoice = new("Ariadne.FinalChoice");

    // ---------------------------------------------------------------------
    // Root / state graph
    // ---------------------------------------------------------------------

    public static IEnumerator<AiStep> Root(AiCtx ctx)
    {
        yield return Ai.Goto("Intro");

        while (true)
            yield return Ai.Wait(999f);
    }

    public static IEnumerator<AiStep> Intro(AiCtx ctx)
    {
        yield return Diag.Line("The palace is quiet in the deliberate way of places that expect blood by morning.", speaker: "Narrator");
        yield return Diag.Line("On the table before you lies a coil of thread, pale as moonlit bone.", speaker: "Narrator");
        yield return Diag.Line("Below your chamber, beyond torchlight and carved stone, the labyrinth waits.", speaker: "Narrator");
        yield return Diag.Line("By dawn, either a hero will be made there, or a myth will crack open.", speaker: "Narrator");
        yield return Ai.Goto("Chamber");
    }

    // ---------------------------------------------------------------------
    // Scene 1: The Chamber
    // ---------------------------------------------------------------------

    public static IEnumerator<AiStep> Chamber(AiCtx ctx)
    {
        while (true)
        {
            var options = new List<DiagChoice>();

            if (!ctx.Bb.GetOrDefault(SeenThread, false))
                options.Add(Diag.Option("thread", "Examine the thread"));
            if (!ctx.Bb.GetOrDefault(SeenKnife, false))
                options.Add(Diag.Option("knife", "Examine the knife"));
            if (!ctx.Bb.GetOrDefault(SeenTablets, false))
                options.Add(Diag.Option("tablets", "Read the tribute tablets"));
            if (!ctx.Bb.GetOrDefault(SeenShrine, false))
                options.Add(Diag.Option("shrine", "Visit the shrine"));

            options.Add(Diag.Option("theseus", "Admit Theseus"));

            yield return Diag.Choose(
                "Your chamber holds its breath. What do you do?",
                options,
                ChamberChoice);

            var choice = ctx.Bb.GetOrDefault(ChamberChoice, "");

            switch (choice)
            {
                case "thread":
                    yield return Ai.Push("InspectThread");
                    break;

                case "knife":
                    yield return Ai.Push("InspectKnife");
                    break;

                case "tablets":
                    yield return Ai.Push("ReadTablets");
                    break;

                case "shrine":
                    yield return Ai.Push("VisitShrine");
                    break;

                case "theseus":
                    yield return Diag.Line("You send word. If he was waiting for courage, it was never his that delayed him.", speaker: "Narrator");
                    yield return Ai.Goto("Theseus");
                    yield break;
            }
        }
    }

    public static IEnumerator<AiStep> InspectThread(AiCtx ctx)
    {
        ctx.Bb.Set(SeenThread, true);
        ctx.Bb.Set(ThreadPrepared, true);
        ctx.Bb.Set(DefiesMinos, true);

        yield return Diag.Line("The thread is finer than it appears. It catches at your skin as if it wants to remember being part of something living.", speaker: "Narrator");
        yield return Diag.Line("A simple thing, in a way. A spool. A line. A small rebellion dressed as household craft.", speaker: "Ariadne");
        yield return Diag.Line("If you place it in Theseus' hands, you do more than guide him. You choose against your father's design.", speaker: "Narrator");

        if (!ctx.Bb.GetOrDefault(WantsEscape, false))
        {
            yield return Diag.Choose(
                "What is the thread to you?",
                [
                    Diag.Option("weapon", "A weapon against the palace"),
                    Diag.Option("path", "A path out"),
                    Diag.Option("mercy", "A chance to spare someone"),
                ],
                ChamberChoice);

            var response = ctx.Bb.GetOrDefault(ChamberChoice, "");
            if (response == "path")
                ctx.Bb.Set(WantsEscape, true);
            if (response == "mercy")
                ctx.Bb.Set(PitiesMinotaur, true);
        }

        yield return Ai.Pop();
    }

    public static IEnumerator<AiStep> InspectKnife(AiCtx ctx)
    {
        ctx.Bb.Set(SeenKnife, true);
        ctx.Bb.Set(KnifeTaken, true);

        yield return Diag.Line("The knife was ceremonial once. Gold at the hilt. A thin curve made for ritual, not war.", speaker: "Narrator");
        yield return Diag.Line("Still, a hand can teach any blade a harsher purpose.", speaker: "Ariadne");
        yield return Diag.Line("You slide it into your sash. The chamber feels different after that, as if it has accepted that words may fail.", speaker: "Narrator");

        yield return Ai.Pop();
    }

    public static IEnumerator<AiStep> ReadTablets(AiCtx ctx)
    {
        ctx.Bb.Set(SeenTablets, true);
        ctx.Bb.Set(PitiesMinotaur, true);
        ctx.Bb.Set(DefiesMinos, true);

        yield return Diag.Line("The tribute tablets are all neat columns and careful names. Boys. Girls. Cities reduced to arithmetic.", speaker: "Narrator");
        yield return Diag.Line("Every generation calls horror necessary in a cleaner hand than the last.", speaker: "Ariadne");
        yield return Diag.Line("For the first time that night, the thing below does not seem like the only creature trapped by the labyrinth.", speaker: "Narrator");

        yield return Ai.Pop();
    }

    public static IEnumerator<AiStep> VisitShrine(AiCtx ctx)
    {
        ctx.Bb.Set(SeenShrine, true);
        ctx.Bb.Set(ShrineVisited, true);
        ctx.Bb.Set(AdmittedFear, true);

        yield return Diag.Line("The shrine is small enough to insult a god and old enough to survive the insult.", speaker: "Narrator");
        yield return Diag.Line("You kneel anyway.", speaker: "Narrator");
        yield return Diag.Line("Not because you expect rescue. Because naming fear aloud is sometimes the only way to stop serving it.", speaker: "Ariadne");

        if (!ctx.Bb.GetOrDefault(WantsEscape, false))
        {
            yield return Diag.Choose(
                "What do you confess there?",
                [
                    Diag.Option("fear", "That you are afraid"),
                    Diag.Option("leave", "That you want to leave Crete"),
                    Diag.Option("mercy", "That the thing below may deserve mercy"),
                ],
                ChamberChoice);

            var response = ctx.Bb.GetOrDefault(ChamberChoice, "");
            if (response == "leave")
                ctx.Bb.Set(WantsEscape, true);
            if (response == "mercy")
                ctx.Bb.Set(PitiesMinotaur, true);
        }

        yield return Diag.Line("When you rise, nothing has been solved. But something in you has stopped pretending to be stone.", speaker: "Narrator");
        yield return Ai.Pop();
    }

    // ---------------------------------------------------------------------
    // Scene 2: Theseus
    // ---------------------------------------------------------------------

    public static IEnumerator<AiStep> Theseus(AiCtx ctx)
    {
        yield return Diag.Line("He comes without escort, which is either brave or theatrical.", speaker: "Narrator");
        yield return Diag.Line("Theseus pauses just inside the chamber door, as if he has entered a temple and is not certain whether he means to pray or steal.", speaker: "Narrator");
        yield return Diag.Line("Princess.", speaker: "Theseus");

        while (true)
        {
            var options = new List<DiagChoice>();

            if (!ctx.Bb.GetOrDefault(AskedWhy, false))
                options.Add(Diag.Option("why", "Ask why he came"));
            if (!ctx.Bb.GetOrDefault(AskedFear, false))
                options.Add(Diag.Option("fear", "Ask whether he fears death"));
            if (!ctx.Bb.GetOrDefault(AskedMonster, false))
                options.Add(Diag.Option("monster", "Ask what he thinks waits below"));
            if (!ctx.Bb.GetOrDefault(AskedPromise, false))
                options.Add(Diag.Option("promise", "Demand a promise"));

            options.Add(Diag.Option("offer", "Decide what help to offer"));

            yield return Diag.Choose(
                "What do you say to Theseus?",
                options,
                TheseusChoice);

            var choice = ctx.Bb.GetOrDefault(TheseusChoice, "");

            switch (choice)
            {
                case "why":
                    yield return Ai.Push("TalkToTheseusWhy");
                    break;

                case "fear":
                    yield return Ai.Push("TalkToTheseusFear");
                    break;

                case "monster":
                    yield return Ai.Push("TalkToTheseusMonster");
                    break;

                case "promise":
                    yield return Ai.Push("DemandPromise");
                    break;

                case "offer":
                    yield return Diag.Choose(
                        "What do you offer him?",
                        [
                            Diag.Option("help", "Offer real help"),
                            Diag.Option("withhold", "Withhold help for now"),
                            Diag.Option("escape", "Speak of fleeing once this is done"),
                        ],
                        TheseusChoice);

                    var offer = ctx.Bb.GetOrDefault(TheseusChoice, "");
                    if (offer == "help")
                    {
                        ctx.Bb.Set(DefiesMinos, true);
                        yield return Diag.Line("Then I will not send you below empty-handed.", speaker: "Ariadne");
                        if (ctx.Bb.GetOrDefault(ThreadPrepared, false))
                            yield return Diag.Line("You let him see the thread. His face changes; not softer, but more mortal.", speaker: "Narrator");
                    }
                    else if (offer == "withhold")
                    {
                        ctx.Bb.Set(TheseusFailedTest, true);
                        yield return Diag.Line("Not yet.", speaker: "Ariadne");
                        yield return Diag.Line("He tries not to look offended. Heroes always think delay is an insult, never a test.", speaker: "Narrator");
                    }
                    else if (offer == "escape")
                    {
                        ctx.Bb.Set(WantsEscape, true);
                        yield return Diag.Line("If the palace opens a door tonight, I may not be here when it closes.", speaker: "Ariadne");
                        yield return Diag.Line("He studies you then as if he has finally understood that the labyrinth is not the only prison in this story.", speaker: "Narrator");
                    }

                    yield return Diag.Line("Enough words. The stones below are listening.", speaker: "Theseus");
                    yield return Ai.Goto("Threshold");
                    yield break;
            }
        }
    }

    public static IEnumerator<AiStep> TalkToTheseusWhy(AiCtx ctx)
    {
        ctx.Bb.Set(AskedWhy, true);

        yield return Diag.Line("Why did you come, truly? For Athens? For glory? For the pleasure of being remembered?", speaker: "Ariadne");
        yield return Diag.Line("If glory were enough, I could have found it somewhere safer.", speaker: "Theseus");
        yield return Diag.Line("Athens sends its children here in chains. I came because I was tired of hearing the word tribute spoken as though it were weather.", speaker: "Theseus");

        if (ctx.Bb.GetOrDefault(ShrineVisited, false))
        {
            ctx.Bb.Set(TrustsTheseus, true);
            yield return Diag.Line("It is not a perfect answer. That is why you believe it a little.", speaker: "Narrator");
        }
        else
        {
            yield return Diag.Line("Perhaps he means it. Perhaps he has learned how men sound when they mean to be trusted.", speaker: "Narrator");
        }

        yield return Ai.Pop();
    }

    public static IEnumerator<AiStep> TalkToTheseusFear(AiCtx ctx)
    {
        ctx.Bb.Set(AskedFear, true);

        yield return Diag.Line("Do you fear death?", speaker: "Ariadne");
        yield return Diag.Line("Yes.", speaker: "Theseus");
        yield return Diag.Line("I only mistrust men who say otherwise.", speaker: "Theseus");

        ctx.Bb.Set(TrustsTheseus, true);

        if (ctx.Bb.GetOrDefault(AdmittedFear, false))
            yield return Diag.Line("Because you named your own fear before he arrived, his honesty feels less like weakness than kinship.", speaker: "Narrator");

        yield return Ai.Pop();
    }

    public static IEnumerator<AiStep> TalkToTheseusMonster(AiCtx ctx)
    {
        ctx.Bb.Set(AskedMonster, true);

        yield return Diag.Line("What do you think waits below?", speaker: "Ariadne");
        yield return Diag.Line("Something made into a story so that everyone responsible for it can sleep.", speaker: "Theseus");

        if (ctx.Bb.GetOrDefault(PitiesMinotaur, false))
        {
            ctx.Bb.Set(TrustsTheseus, true);
            yield return Diag.Line("Not a beast, then?", speaker: "Ariadne");
            yield return Diag.Line("A beast, a man, a punishment, a child. I do not know. I only know that naming it monster does not explain the hands that built the maze.", speaker: "Theseus");
        }
        else
        {
            yield return Diag.Line("You had expected something cleaner from him. Sword answers. Hero answers. Instead he leaves you with a human shape where a monster should have been.", speaker: "Narrator");
            ctx.Bb.Set(PitiesMinotaur, true);
        }

        yield return Ai.Pop();
    }

    public static IEnumerator<AiStep> DemandPromise(AiCtx ctx)
    {
        ctx.Bb.Set(AskedPromise, true);

        yield return Diag.Line("If I help you, you do not get to descend as legend only. You go as a man under oath.", speaker: "Ariadne");
        yield return Diag.Choose(
            "What promise do you demand?",
            [
                Diag.Option("mercy", "Spare the creature if there is any human mind left in it"),
                Diag.Option("truth", "Speak my name truthfully in whatever story survives"),
                Diag.Option("take", "Take me with you if you live"),
            ],
            TheseusChoice);

        var promise = ctx.Bb.GetOrDefault(TheseusChoice, "");

        if (promise == "mercy")
        {
            ctx.Bb.Set(PromisedMercy, true);
            ctx.Bb.Set(PitiesMinotaur, true);
            yield return Diag.Line("If there is mercy possible in that place, I will not refuse it.", speaker: "Theseus");
        }
        else if (promise == "truth")
        {
            yield return Diag.Line("Then let no poet make me larger than the women who kept me alive tonight.", speaker: "Theseus");
            ctx.Bb.Set(TrustsTheseus, true);
        }
        else if (promise == "take")
        {
            ctx.Bb.Set(WantsEscape, true);
            yield return Diag.Line("If I walk back into the light, I will not leave you in this house of debts.", speaker: "Theseus");
        }

        yield return Ai.Pop();
    }

    // ---------------------------------------------------------------------
    // Scene 3: Threshold
    // ---------------------------------------------------------------------

    public static IEnumerator<AiStep> Threshold(AiCtx ctx)
    {
        yield return Diag.Line("At the threshold of the labyrinth, torchlight becomes hesitant.", speaker: "Narrator");
        yield return Diag.Line("The sealed stone below the palace seems less like an entrance than a held breath.", speaker: "Narrator");

        var options = new List<DiagChoice>();

        if (ctx.Bb.GetOrDefault(ThreadPrepared, false))
            options.Add(Diag.Option("help_theseus", "Place the thread in Theseus' hand"));
        else
            options.Add(Diag.Option("help_theseus", "Send Theseus below with only your blessing"));

        options.Add(Diag.Option("warn_asterion", "Go below to warn the thing in the dark"));
        options.Add(Diag.Option("go_alone", "Take the thread and descend yourself"));
        options.Add(Diag.Option("stay_and_rule", "Turn back toward the palace"));

        yield return Diag.Choose(
            "What story do you choose?",
            options,
            FinalChoice);

        var decision = ctx.Bb.GetOrDefault(FinalChoice, "");

        switch (decision)
        {
            case "help_theseus":
                yield return Ai.Goto("Ending_ThreadAndFlight");
                yield break;

            case "warn_asterion":
                yield return Ai.Goto("Ending_MercyInTheDark");
                yield break;

            case "go_alone":
                yield return Ai.Goto("Ending_TheDescent");
                yield break;

            case "stay_and_rule":
                yield return Ai.Goto("Ending_CrownOfKnives");
                yield break;

            default:
                yield return Ai.Goto("Ending_ThreadlessTragedy");
                yield break;
        }
    }

    // ---------------------------------------------------------------------
    // Endings
    // ---------------------------------------------------------------------

    public static IEnumerator<AiStep> Ending_ThreadAndFlight(AiCtx ctx)
    {
        yield return Diag.Line("You place the thread in his hand.", speaker: "Narrator");

        if (ctx.Bb.GetOrDefault(ThreadPrepared, false))
            yield return Diag.Line("It runs from your fingers to his like a vow too practical to call sacred, and too sacred to call mere thread.", speaker: "Narrator");

        if (ctx.Bb.GetOrDefault(PromisedMercy, false))
            yield return Diag.Line("Before he disappears into the dark, he repeats the promise back to you. Not loudly. As if afraid the stone might overhear and mock him.", speaker: "Narrator");

        if (ctx.Bb.GetOrDefault(WantsEscape, false))
        {
            yield return Diag.Line("When the palace wakes to its own undoing, you do not wait to be thanked. You go with the surf, with the blood, with the unfinished name of yourself.", speaker: "Narrator");

            if (ctx.Bb.GetOrDefault(TrustsTheseus, false) && !ctx.Bb.GetOrDefault(TheseusFailedTest, false))
                yield return Diag.Line("Whether you loved him or only believed him for one necessary hour no poet will ever say correctly.", speaker: "Narrator");
            else
                yield return Diag.Line("You do not mistake motion for love. Still, departure can be holy even when the companion is not.", speaker: "Narrator");
        }
        else
        {
            yield return Diag.Line("You remain long enough to hear the first cry rise from below, then another, then silence. By dawn the myth belongs to men again, but never entirely.", speaker: "Narrator");
        }

        yield return Diag.Line("Ending: Thread and Flight", speaker: "System");
        ctx.Bb.Set(AdventureComplete, true);
        yield return Ai.Succeed();
    }

    public static IEnumerator<AiStep> Ending_MercyInTheDark(AiCtx ctx)
    {
        yield return Diag.Line("You choose the dark not to conquer it, but to warn what waits inside.", speaker: "Narrator");
        yield return Diag.Line("Asterion is not what the songs would have preferred. That is the first true thing the night gives you.", speaker: "Narrator");
        yield return Diag.Line("The labyrinth was built to make everyone simple: beast, maiden, king, hero. Beneath the palace, none of those names survive their first honest echo.", speaker: "Narrator");

        if (ctx.Bb.GetOrDefault(PromisedMercy, false))
            yield return Diag.Line("Whether mercy arrives in time is a matter for another telling. But you have broken the old obedience, and that is how new myths begin.", speaker: "Narrator");
        else
            yield return Diag.Line("No one above will call what you did mercy. They will call it treason, madness, softness. Let them. They built a maze and mistook themselves for civilized.", speaker: "Narrator");

        yield return Diag.Line("Ending: Mercy in the Dark", speaker: "System");
        ctx.Bb.Set(AdventureComplete, true);
        yield return Ai.Succeed();
    }

    public static IEnumerator<AiStep> Ending_CrownOfKnives(AiCtx ctx)
    {
        yield return Diag.Line("You turn back toward the palace.", speaker: "Narrator");
        yield return Diag.Line("Not because you believe it innocent. Because you finally understand that innocence was never one of the rooms it offered you.", speaker: "Narrator");

        if (ctx.Bb.GetOrDefault(KnifeTaken, false))
            yield return Diag.Line("The knife at your side is no longer ceremonial. Neither, perhaps, are you.", speaker: "Narrator");

        yield return Diag.Line("Men below and men above will finish making a legend of each other. You will remain to govern what legends leave behind: widows, walls, frightened servants, and the throne itself.", speaker: "Narrator");
        yield return Diag.Line("Ending: Crown of Knives", speaker: "System");
        ctx.Bb.Set(AdventureComplete, true);
        yield return Ai.Succeed();
    }

    public static IEnumerator<AiStep> Ending_TheDescent(AiCtx ctx)
    {
        yield return Diag.Line("You take the thread yourself.", speaker: "Narrator");

        if (ctx.Bb.GetOrDefault(KnifeTaken, false))
            yield return Diag.Line("The knife is warm against your side. The thread is cool in your hand. Between them you feel, for the first time that night, perfectly balanced.", speaker: "Narrator");
        else
            yield return Diag.Line("No blade. Only a thread and the insolence to descend where history expected you to remain a witness.", speaker: "Narrator");

        yield return Diag.Line("There are stories in which Ariadne waits at the edge and stories in which heroes decide what the dark contains. This is not one of them.", speaker: "Narrator");
        yield return Diag.Line("You step below, and the labyrinth receives not a victim, not a bride, not a guide, but its first honest heir.", speaker: "Narrator");
        yield return Diag.Line("Ending: The Descent", speaker: "System");
        ctx.Bb.Set(AdventureComplete, true);
        yield return Ai.Succeed();
    }

    public static IEnumerator<AiStep> Ending_ThreadlessTragedy(AiCtx ctx)
    {
        yield return Diag.Line("Morning comes whether or not anyone is ready for it.", speaker: "Narrator");
        yield return Diag.Line("By the time the palace doors open, choice has already hardened into consequence somewhere you cannot reach.", speaker: "Narrator");
        yield return Diag.Line("When people refuse to choose a story, the cruelest one often chooses itself.", speaker: "Narrator");
        yield return Diag.Line("Ending: Threadless Tragedy", speaker: "System");
        ctx.Bb.Set(AdventureComplete, true);
        yield return Ai.Succeed();
    }
}