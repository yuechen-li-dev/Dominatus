using Ariadne.OptFlow;
using Ariadne.OptFlow.Commands;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.OptFlow;

namespace Ariadne.ConsoleApp.Scripts;

public static partial class AriadneThreadOfNight
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

    [DominatusState("Root", Root = true)]
    public static IEnumerator<AiStep> Root(AiCtx ctx)
    {
        yield return Ai.Goto(States.Intro);

        yield return Ai.Steady("Root parked after handoff");
    }

    [DominatusState("Intro")]
    public static IEnumerator<AiStep> Intro(AiCtx ctx)
    {
        yield return Diag.Line(id: "thread.intro.the-palace-is-quiet-in-the-deliberat", "The palace is quiet in the deliberate way of places that expect blood by morning.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.intro.on-the-table-before-you-lies-a-coil", "On the table before you lies a coil of thread, pale as moonlit bone.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.intro.below-your-chamber-beyond-torchlight", "Below your chamber, beyond torchlight and carved stone, the labyrinth waits.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.intro.by-dawn-either-a-hero-will-be-made-t", "By dawn, either a hero will be made there, or a myth will crack open.", speaker: "Narrator");
        yield return Ai.Goto(States.Chamber);
    }

    // ---------------------------------------------------------------------
    // Scene 1: The Chamber
    // ---------------------------------------------------------------------

    [DominatusState("Chamber")]
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

            yield return Diag.Choose(id: "thread.chamber.your-chamber-holds-its-breath-what-d",
                "Your chamber holds its breath. What do you do?",
                options,
                ChamberChoice);

            var choice = ctx.Bb.GetOrDefault(ChamberChoice, "");

            switch (choice)
            {
                case "thread":
                    yield return Ai.Push(States.InspectThread);
                    break;

                case "knife":
                    yield return Ai.Push(States.InspectKnife);
                    break;

                case "tablets":
                    yield return Ai.Push(States.ReadTablets);
                    break;

                case "shrine":
                    yield return Ai.Push(States.VisitShrine);
                    break;

                case "theseus":
                    yield return Diag.Line(id: "thread.chamber.you-send-word-if-he-was-waiting-for", "You send word. If he was waiting for courage, it was never his that delayed him.", speaker: "Narrator");
                    yield return Ai.Goto(States.Theseus);
                    yield break;
            }
        }
    }

    [DominatusState("InspectThread")]
    public static IEnumerator<AiStep> InspectThread(AiCtx ctx)
    {
        ctx.Bb.Set(SeenThread, true);
        ctx.Bb.Set(ThreadPrepared, true);
        ctx.Bb.Set(DefiesMinos, true);

        yield return Diag.Line(id: "thread.inspect-thread.the-thread-is-finer-than-it-appears", "The thread is finer than it appears. It catches at your skin as if it wants to remember being part of something living.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.inspect-thread.a-simple-thing-in-a-way-a-spool-a-li", "A simple thing, in a way. A spool. A line. A small rebellion dressed as household craft.", speaker: "Ariadne");
        yield return Diag.Line(id: "thread.inspect-thread.if-you-place-it-in-theseus-hands-you", "If you place it in Theseus' hands, you do more than guide him. You choose against your father's design.", speaker: "Narrator");

        if (!ctx.Bb.GetOrDefault(WantsEscape, false))
        {
            yield return Diag.Choose(id: "thread.inspect-thread.what-is-the-thread-to-you",
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

    [DominatusState("InspectKnife")]
    public static IEnumerator<AiStep> InspectKnife(AiCtx ctx)
    {
        ctx.Bb.Set(SeenKnife, true);
        ctx.Bb.Set(KnifeTaken, true);

        yield return Diag.Line(id: "thread.inspect-knife.the-knife-was-ceremonial-once-gold-a", "The knife was ceremonial once. Gold at the hilt. A thin curve made for ritual, not war.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.inspect-knife.still-a-hand-can-teach-any-blade-a-h", "Still, a hand can teach any blade a harsher purpose.", speaker: "Ariadne");
        yield return Diag.Line(id: "thread.inspect-knife.you-slide-it-into-your-sash-the-cham", "You slide it into your sash. The chamber feels different after that, as if it has accepted that words may fail.", speaker: "Narrator");

        yield return Ai.Pop();
    }

    [DominatusState("ReadTablets")]
    public static IEnumerator<AiStep> ReadTablets(AiCtx ctx)
    {
        ctx.Bb.Set(SeenTablets, true);
        ctx.Bb.Set(PitiesMinotaur, true);
        ctx.Bb.Set(DefiesMinos, true);

        yield return Diag.Line(id: "thread.read-tablets.the-tribute-tablets-are-all-neat-col", "The tribute tablets are all neat columns and careful names. Boys. Girls. Cities reduced to arithmetic.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.read-tablets.every-generation-calls-horror-necess", "Every generation calls horror necessary in a cleaner hand than the last.", speaker: "Ariadne");
        yield return Diag.Line(id: "thread.read-tablets.for-the-first-time-that-night-the-th", "For the first time that night, the thing below does not seem like the only creature trapped by the labyrinth.", speaker: "Narrator");

        yield return Ai.Pop();
    }

    [DominatusState("VisitShrine")]
    public static IEnumerator<AiStep> VisitShrine(AiCtx ctx)
    {
        ctx.Bb.Set(SeenShrine, true);
        ctx.Bb.Set(ShrineVisited, true);
        ctx.Bb.Set(AdmittedFear, true);

        yield return Diag.Line(id: "thread.visit-shrine.the-shrine-is-small-enough-to-insult", "The shrine is small enough to insult a god and old enough to survive the insult.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.visit-shrine.you-kneel-anyway", "You kneel anyway.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.visit-shrine.not-because-you-expect-rescue-becaus", "Not because you expect rescue. Because naming fear aloud is sometimes the only way to stop serving it.", speaker: "Ariadne");

        if (!ctx.Bb.GetOrDefault(WantsEscape, false))
        {
            yield return Diag.Choose(id: "thread.visit-shrine.what-do-you-confess-there",
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

        yield return Diag.Line(id: "thread.visit-shrine.when-you-rise-nothing-has-been-solve", "When you rise, nothing has been solved. But something in you has stopped pretending to be stone.", speaker: "Narrator");
        yield return Ai.Pop();
    }

    // ---------------------------------------------------------------------
    // Scene 2: Theseus
    // ---------------------------------------------------------------------

    [DominatusState("Theseus")]
    public static IEnumerator<AiStep> Theseus(AiCtx ctx)
    {
        yield return Diag.Line(id: "thread.theseus.he-comes-without-escort-which-is-eit", "He comes without escort, which is either brave or theatrical.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.theseus.theseus-pauses-just-inside-the-chamb", "Theseus pauses just inside the chamber door, as if he has entered a temple and is not certain whether he means to pray or steal.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.theseus.princess", "Princess.", speaker: "Theseus");

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

            yield return Diag.Choose(id: "thread.theseus.what-do-you-say-to-theseus",
                "What do you say to Theseus?",
                options,
                TheseusChoice);

            var choice = ctx.Bb.GetOrDefault(TheseusChoice, "");

            switch (choice)
            {
                case "why":
                    yield return Ai.Push(States.TalkToTheseusWhy);
                    break;

                case "fear":
                    yield return Ai.Push(States.TalkToTheseusFear);
                    break;

                case "monster":
                    yield return Ai.Push(States.TalkToTheseusMonster);
                    break;

                case "promise":
                    yield return Ai.Push(States.DemandPromise);
                    break;

                case "offer":
                    yield return Diag.Choose(id: "thread.theseus.what-do-you-offer-him",
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
                        yield return Diag.Line(id: "thread.theseus.then-i-will-not-send-you-below-empty", "Then I will not send you below empty-handed.", speaker: "Ariadne");
                        if (ctx.Bb.GetOrDefault(ThreadPrepared, false))
                            yield return Diag.Line(id: "thread.theseus.you-let-him-see-the-thread-his-face", "You let him see the thread. His face changes; not softer, but more mortal.", speaker: "Narrator");
                    }
                    else if (offer == "withhold")
                    {
                        ctx.Bb.Set(TheseusFailedTest, true);
                        yield return Diag.Line(id: "thread.theseus.not-yet", "Not yet.", speaker: "Ariadne");
                        yield return Diag.Line(id: "thread.theseus.he-tries-not-to-look-offended-heroes", "He tries not to look offended. Heroes always think delay is an insult, never a test.", speaker: "Narrator");
                    }
                    else if (offer == "escape")
                    {
                        ctx.Bb.Set(WantsEscape, true);
                        yield return Diag.Line(id: "thread.theseus.if-the-palace-opens-a-door-tonight-i", "If the palace opens a door tonight, I may not be here when it closes.", speaker: "Ariadne");
                        yield return Diag.Line(id: "thread.theseus.he-studies-you-then-as-if-he-has-fin", "He studies you then as if he has finally understood that the labyrinth is not the only prison in this story.", speaker: "Narrator");
                    }

                    yield return Diag.Line(id: "thread.theseus.enough-words-the-stones-below-are-li", "Enough words. The stones below are listening.", speaker: "Theseus");
                    yield return Ai.Goto(States.Threshold);
                    yield break;
            }
        }
    }

    [DominatusState("TalkToTheseusWhy")]
    public static IEnumerator<AiStep> TalkToTheseusWhy(AiCtx ctx)
    {
        ctx.Bb.Set(AskedWhy, true);

        yield return Diag.Line(id: "thread.talk-to-theseus-why.why-did-you-come-truly-for-athens-fo", "Why did you come, truly? For Athens? For glory? For the pleasure of being remembered?", speaker: "Ariadne");
        yield return Diag.Line(id: "thread.talk-to-theseus-why.if-glory-were-enough-i-could-have-fo", "If glory were enough, I could have found it somewhere safer.", speaker: "Theseus");
        yield return Diag.Line(id: "thread.talk-to-theseus-why.athens-sends-its-children-here-in-ch", "Athens sends its children here in chains. I came because I was tired of hearing the word tribute spoken as though it were weather.", speaker: "Theseus");

        if (ctx.Bb.GetOrDefault(ShrineVisited, false))
        {
            ctx.Bb.Set(TrustsTheseus, true);
            yield return Diag.Line(id: "thread.talk-to-theseus-why.it-is-not-a-perfect-answer-that-is-w", "It is not a perfect answer. That is why you believe it a little.", speaker: "Narrator");
        }
        else
        {
            yield return Diag.Line(id: "thread.talk-to-theseus-why.perhaps-he-means-it-perhaps-he-has-l", "Perhaps he means it. Perhaps he has learned how men sound when they mean to be trusted.", speaker: "Narrator");
        }

        yield return Ai.Pop();
    }

    [DominatusState("TalkToTheseusFear")]
    public static IEnumerator<AiStep> TalkToTheseusFear(AiCtx ctx)
    {
        ctx.Bb.Set(AskedFear, true);

        yield return Diag.Line(id: "thread.talk-to-theseus-fear.do-you-fear-death", "Do you fear death?", speaker: "Ariadne");
        yield return Diag.Line(id: "thread.talk-to-theseus-fear.yes", "Yes.", speaker: "Theseus");
        yield return Diag.Line(id: "thread.talk-to-theseus-fear.i-only-mistrust-men-who-say-otherwis", "I only mistrust men who say otherwise.", speaker: "Theseus");

        ctx.Bb.Set(TrustsTheseus, true);

        if (ctx.Bb.GetOrDefault(AdmittedFear, false))
            yield return Diag.Line(id: "thread.talk-to-theseus-fear.because-you-named-your-own-fear-befo", "Because you named your own fear before he arrived, his honesty feels less like weakness than kinship.", speaker: "Narrator");

        yield return Ai.Pop();
    }

    [DominatusState("TalkToTheseusMonster")]
    public static IEnumerator<AiStep> TalkToTheseusMonster(AiCtx ctx)
    {
        ctx.Bb.Set(AskedMonster, true);

        yield return Diag.Line(id: "thread.talk-to-theseus-monster.what-do-you-think-waits-below", "What do you think waits below?", speaker: "Ariadne");
        yield return Diag.Line(id: "thread.talk-to-theseus-monster.something-made-into-a-story-so-that", "Something made into a story so that everyone responsible for it can sleep.", speaker: "Theseus");

        if (ctx.Bb.GetOrDefault(PitiesMinotaur, false))
        {
            ctx.Bb.Set(TrustsTheseus, true);
            yield return Diag.Line(id: "thread.talk-to-theseus-monster.not-a-beast-then", "Not a beast, then?", speaker: "Ariadne");
            yield return Diag.Line(id: "thread.talk-to-theseus-monster.a-beast-a-man-a-punishment-a-child-i", "A beast, a man, a punishment, a child. I do not know. I only know that naming it monster does not explain the hands that built the maze.", speaker: "Theseus");
        }
        else
        {
            yield return Diag.Line(id: "thread.talk-to-theseus-monster.you-had-expected-something-cleaner-f", "You had expected something cleaner from him. Sword answers. Hero answers. Instead he leaves you with a human shape where a monster should have been.", speaker: "Narrator");
            ctx.Bb.Set(PitiesMinotaur, true);
        }

        yield return Ai.Pop();
    }

    [DominatusState("DemandPromise")]
    public static IEnumerator<AiStep> DemandPromise(AiCtx ctx)
    {
        ctx.Bb.Set(AskedPromise, true);

        yield return Diag.Line(id: "thread.demand-promise.if-i-help-you-you-do-not-get-to-desc", "If I help you, you do not get to descend as legend only. You go as a man under oath.", speaker: "Ariadne");
        yield return Diag.Choose(id: "thread.demand-promise.what-promise-do-you-demand",
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
            yield return Diag.Line(id: "thread.demand-promise.if-there-is-mercy-possible-in-that-p", "If there is mercy possible in that place, I will not refuse it.", speaker: "Theseus");
        }
        else if (promise == "truth")
        {
            yield return Diag.Line(id: "thread.demand-promise.then-let-no-poet-make-me-larger-than", "Then let no poet make me larger than the women who kept me alive tonight.", speaker: "Theseus");
            ctx.Bb.Set(TrustsTheseus, true);
        }
        else if (promise == "take")
        {
            ctx.Bb.Set(WantsEscape, true);
            yield return Diag.Line(id: "thread.demand-promise.if-i-walk-back-into-the-light-i-will", "If I walk back into the light, I will not leave you in this house of debts.", speaker: "Theseus");
        }

        yield return Ai.Pop();
    }

    // ---------------------------------------------------------------------
    // Scene 3: Threshold
    // ---------------------------------------------------------------------

    [DominatusState("Threshold")]
    public static IEnumerator<AiStep> Threshold(AiCtx ctx)
    {
        yield return Diag.Line(id: "thread.threshold.at-the-threshold-of-the-labyrinth-to", "At the threshold of the labyrinth, torchlight becomes hesitant.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.threshold.the-sealed-stone-below-the-palace-se", "The sealed stone below the palace seems less like an entrance than a held breath.", speaker: "Narrator");

        var options = new List<DiagChoice>();

        if (ctx.Bb.GetOrDefault(ThreadPrepared, false))
            options.Add(Diag.Option("help_theseus", "Place the thread in Theseus' hand"));
        else
            options.Add(Diag.Option("help_theseus", "Send Theseus below with only your blessing"));

        options.Add(Diag.Option("warn_asterion", "Go below to warn the thing in the dark"));
        options.Add(Diag.Option("go_alone", "Take the thread and descend yourself"));
        options.Add(Diag.Option("stay_and_rule", "Turn back toward the palace"));

        yield return Diag.Choose(id: "thread.threshold.what-story-do-you-choose",
            "What story do you choose?",
            options,
            FinalChoice);

        var decision = ctx.Bb.GetOrDefault(FinalChoice, "");

        switch (decision)
        {
            case "help_theseus":
                yield return Ai.Goto(States.Ending_ThreadAndFlight);
                yield break;

            case "warn_asterion":
                yield return Ai.Goto(States.Ending_MercyInTheDark);
                yield break;

            case "go_alone":
                yield return Ai.Goto(States.Ending_TheDescent);
                yield break;

            case "stay_and_rule":
                yield return Ai.Goto(States.Ending_CrownOfKnives);
                yield break;

            default:
                yield return Ai.Goto(States.Ending_ThreadlessTragedy);
                yield break;
        }
    }

    // ---------------------------------------------------------------------
    // Endings
    // ---------------------------------------------------------------------

    [DominatusState("Ending_ThreadAndFlight")]
    public static IEnumerator<AiStep> Ending_ThreadAndFlight(AiCtx ctx)
    {
        yield return Diag.Line(id: "thread.ending_thread-and-flight.you-place-the-thread-in-his-hand", "You place the thread in his hand.", speaker: "Narrator");

        if (ctx.Bb.GetOrDefault(ThreadPrepared, false))
            yield return Diag.Line(id: "thread.ending_thread-and-flight.it-runs-from-your-fingers-to-his-lik", "It runs from your fingers to his like a vow too practical to call sacred, and too sacred to call mere thread.", speaker: "Narrator");

        if (ctx.Bb.GetOrDefault(PromisedMercy, false))
            yield return Diag.Line(id: "thread.ending_thread-and-flight.before-he-disappears-into-the-dark-h", "Before he disappears into the dark, he repeats the promise back to you. Not loudly. As if afraid the stone might overhear and mock him.", speaker: "Narrator");

        if (ctx.Bb.GetOrDefault(WantsEscape, false))
        {
            yield return Diag.Line(id: "thread.ending_thread-and-flight.when-the-palace-wakes-to-its-own-und", "When the palace wakes to its own undoing, you do not wait to be thanked. You go with the surf, with the blood, with the unfinished name of yourself.", speaker: "Narrator");

            if (ctx.Bb.GetOrDefault(TrustsTheseus, false) && !ctx.Bb.GetOrDefault(TheseusFailedTest, false))
                yield return Diag.Line(id: "thread.ending_thread-and-flight.whether-you-loved-him-or-only-believ", "Whether you loved him or only believed him for one necessary hour no poet will ever say correctly.", speaker: "Narrator");
            else
                yield return Diag.Line(id: "thread.ending_thread-and-flight.you-do-not-mistake-motion-for-love-s", "You do not mistake motion for love. Still, departure can be holy even when the companion is not.", speaker: "Narrator");
        }
        else
        {
            yield return Diag.Line(id: "thread.ending_thread-and-flight.you-remain-long-enough-to-hear-the-f", "You remain long enough to hear the first cry rise from below, then another, then silence. By dawn the myth belongs to men again, but never entirely.", speaker: "Narrator");
        }

        yield return Diag.Line(id: "thread.ending_thread-and-flight.ending-thread-and-flight", "Ending: Thread and Flight", speaker: "System");
        ctx.Bb.Set(AdventureComplete, true);
        yield return Ai.Succeed();
    }

    [DominatusState("Ending_MercyInTheDark")]
    public static IEnumerator<AiStep> Ending_MercyInTheDark(AiCtx ctx)
    {
        yield return Diag.Line(id: "thread.ending_mercy-in-the-dark.you-choose-the-dark-not-to-conquer-i", "You choose the dark not to conquer it, but to warn what waits inside.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.ending_mercy-in-the-dark.asterion-is-not-what-the-songs-would", "Asterion is not what the songs would have preferred. That is the first true thing the night gives you.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.ending_mercy-in-the-dark.the-labyrinth-was-built-to-make-ever", "The labyrinth was built to make everyone simple: beast, maiden, king, hero. Beneath the palace, none of those names survive their first honest echo.", speaker: "Narrator");

        if (ctx.Bb.GetOrDefault(PromisedMercy, false))
            yield return Diag.Line(id: "thread.ending_mercy-in-the-dark.whether-mercy-arrives-in-time-is-a-m", "Whether mercy arrives in time is a matter for another telling. But you have broken the old obedience, and that is how new myths begin.", speaker: "Narrator");
        else
            yield return Diag.Line(id: "thread.ending_mercy-in-the-dark.no-one-above-will-call-what-you-did", "No one above will call what you did mercy. They will call it treason, madness, softness. Let them. They built a maze and mistook themselves for civilized.", speaker: "Narrator");

        yield return Diag.Line(id: "thread.ending_mercy-in-the-dark.ending-mercy-in-the-dark", "Ending: Mercy in the Dark", speaker: "System");
        ctx.Bb.Set(AdventureComplete, true);
        yield return Ai.Succeed();
    }

    [DominatusState("Ending_CrownOfKnives")]
    public static IEnumerator<AiStep> Ending_CrownOfKnives(AiCtx ctx)
    {
        yield return Diag.Line(id: "thread.ending_crown-of-knives.you-turn-back-toward-the-palace", "You turn back toward the palace.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.ending_crown-of-knives.not-because-you-believe-it-innocent", "Not because you believe it innocent. Because you finally understand that innocence was never one of the rooms it offered you.", speaker: "Narrator");

        if (ctx.Bb.GetOrDefault(KnifeTaken, false))
            yield return Diag.Line(id: "thread.ending_crown-of-knives.the-knife-at-your-side-is-no-longer", "The knife at your side is no longer ceremonial. Neither, perhaps, are you.", speaker: "Narrator");

        yield return Diag.Line(id: "thread.ending_crown-of-knives.men-below-and-men-above-will-finish", "Men below and men above will finish making a legend of each other. You will remain to govern what legends leave behind: widows, walls, frightened servants, and the throne itself.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.ending_crown-of-knives.ending-crown-of-knives", "Ending: Crown of Knives", speaker: "System");
        ctx.Bb.Set(AdventureComplete, true);
        yield return Ai.Succeed();
    }

    [DominatusState("Ending_TheDescent")]
    public static IEnumerator<AiStep> Ending_TheDescent(AiCtx ctx)
    {
        yield return Diag.Line(id: "thread.ending_the-descent.you-take-the-thread-yourself", "You take the thread yourself.", speaker: "Narrator");

        if (ctx.Bb.GetOrDefault(KnifeTaken, false))
            yield return Diag.Line(id: "thread.ending_the-descent.the-knife-is-warm-against-your-side", "The knife is warm against your side. The thread is cool in your hand. Between them you feel, for the first time that night, perfectly balanced.", speaker: "Narrator");
        else
            yield return Diag.Line(id: "thread.ending_the-descent.no-blade-only-a-thread-and-the-insol", "No blade. Only a thread and the insolence to descend where history expected you to remain a witness.", speaker: "Narrator");

        yield return Diag.Line(id: "thread.ending_the-descent.there-are-stories-in-which-ariadne-w", "There are stories in which Ariadne waits at the edge and stories in which heroes decide what the dark contains. This is not one of them.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.ending_the-descent.you-step-below-and-the-labyrinth-rec", "You step below, and the labyrinth receives not a victim, not a bride, not a guide, but its first honest heir.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.ending_the-descent.ending-the-descent", "Ending: The Descent", speaker: "System");
        ctx.Bb.Set(AdventureComplete, true);
        yield return Ai.Succeed();
    }

    [DominatusState("Ending_ThreadlessTragedy")]
    public static IEnumerator<AiStep> Ending_ThreadlessTragedy(AiCtx ctx)
    {
        yield return Diag.Line(id: "thread.ending_threadless-tragedy.morning-comes-whether-or-not-anyone", "Morning comes whether or not anyone is ready for it.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.ending_threadless-tragedy.by-the-time-the-palace-doors-open-ch", "By the time the palace doors open, choice has already hardened into consequence somewhere you cannot reach.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.ending_threadless-tragedy.when-people-refuse-to-choose-a-story", "When people refuse to choose a story, the cruelest one often chooses itself.", speaker: "Narrator");
        yield return Diag.Line(id: "thread.ending_threadless-tragedy.ending-threadless-tragedy", "Ending: Threadless Tragedy", speaker: "System");
        ctx.Bb.Set(AdventureComplete, true);
        yield return Ai.Succeed();
    }

    public static readonly FlowDefinition Definition = Define();

    [DominatusFlow("ariadne.thread-of-night", KeepRootFrame = true)]
    public static partial FlowDefinition Define();
}
