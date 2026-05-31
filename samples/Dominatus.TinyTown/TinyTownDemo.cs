using System.Text.Json;
using Dominatus.Core;
using Dominatus.Core.Blackboard;
using Dominatus.Core.Decision;
using Dominatus.Core.Hfsm;
using Dominatus.Core.Nodes;
using Dominatus.Core.Nodes.Steps;
using Dominatus.Core.Runtime;
using Dominatus.Llm.OptFlow;
using Dominatus.OptFlow;

namespace Dominatus.TinyTown;

public sealed record TownieProfile(
    string Id,
    string Name,
    string Job,
    string HomeLocation,
    string WorkLocation,
    IReadOnlyList<string> FriendIds,
    TownieTraits Traits,
    WorkSchedule WorkSchedule);

public sealed record TownieTraits(
    bool Social = false,
    bool Introvert = false,
    bool HardWorker = false,
    bool Playful = false,
    bool Creative = false,
    bool Neat = false,
    bool Serious = false);

public sealed record WorkSchedule(int StartTick, int EndTick);

public sealed record FriendVisitRequested(string FromTownieId, string ToTownieId, string Location, int Tick);

public sealed record RelationshipState
{
    public required string A { get; init; }
    public required string B { get; init; }
    public float Affinity { get; init; }
    public float Tension { get; init; }
    public int LastInteractionTick { get; init; }
    public string? UnresolvedIssueId { get; init; }
}

public sealed record RelationshipSnapshot
{
    public required string A { get; init; }
    public required string B { get; init; }
    public required float Affinity { get; init; }
    public required float Tension { get; init; }
    public required int LastInteractionTick { get; init; }
    public string? UnresolvedIssueId { get; init; }
}

public sealed record TownMemoryRecord
{
    public required string Id { get; init; }
    public required int Tick { get; init; }
    public required IReadOnlyList<string> TownieIds { get; init; }
    public required string Summary { get; init; }
    public required string Kind { get; init; }
}

public sealed record DialogueSceneOutcome
{
    public required string Dialogue { get; init; }
    public required string Tone { get; init; }
    public required string Outcome { get; init; }
    public float AffinityDelta { get; init; }
    public float TensionDelta { get; init; }
    public required string MemorySummary { get; init; }
}

public sealed record TinyTownSimulationResult
{
    public required int TicksRun { get; init; }
    public required IReadOnlyList<TownieSnapshot> FinalTownies { get; init; }
    public required IReadOnlyList<string> EventLog { get; init; }
    public required IReadOnlyList<string> DialogueLines { get; init; }
    public required int LlmCallCount { get; init; }
    public required bool UsedAiDecide { get; init; }
    public required IReadOnlyList<RelationshipSnapshot> Relationships { get; init; }
    public required IReadOnlyList<TownMemoryRecord> Memories { get; init; }
    public required IReadOnlyList<DialogueSceneOutcome> DialogueOutcomes { get; init; }
    public required IReadOnlyList<string> LlmCallContexts { get; init; }
}

public sealed record TownieSnapshot
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Location { get; init; }
    public required string CurrentAction { get; init; }
    public required float Hunger { get; init; }
    public required float Energy { get; init; }
    public required float Social { get; init; }
    public required float Fun { get; init; }
    public required float Hygiene { get; init; }
    public required float Bladder { get; init; }
}

public sealed record TinyTownScenarioOptions
{
    public int StartTick { get; init; }
    public IReadOnlyDictionary<string, float>? Hunger { get; init; }
    public IReadOnlyDictionary<string, float>? Energy { get; init; }
    public IReadOnlyDictionary<string, float>? Social { get; init; }
    public IReadOnlyDictionary<string, float>? Fun { get; init; }
    public IReadOnlyDictionary<string, float>? Hygiene { get; init; }
    public IReadOnlyDictionary<string, float>? Bladder { get; init; }
    public IReadOnlyDictionary<string, string>? Locations { get; init; }
    public bool DisableSocialActions { get; init; }
    public string? ForcedDialogueResponseJson { get; init; }
}

public static class TinyTownDemo
{
    public const string Home = "Home";
    public const string Cafe = "Cafe";
    public const string Park = "Park";

    private static readonly BbKey<string> ProfileIdKey = new("TinyTown.ProfileId");
    private static readonly BbKey<string> LocationKey = new("TinyTown.Location");
    private static readonly BbKey<string> CurrentActionKey = new("TinyTown.CurrentAction");
    private static readonly BbKey<float> HungerKey = new("TinyTown.Needs.Hunger");
    private static readonly BbKey<float> EnergyKey = new("TinyTown.Needs.Energy");
    private static readonly BbKey<float> SocialKey = new("TinyTown.Needs.Social");
    private static readonly BbKey<float> FunKey = new("TinyTown.Needs.Fun");
    private static readonly BbKey<float> HygieneKey = new("TinyTown.Needs.Hygiene");
    private static readonly BbKey<float> BladderKey = new("TinyTown.Needs.Bladder");
    private static readonly BbKey<bool> UsedAiDecideKey = new("TinyTown.UsedAiDecide");
    private static readonly BbKey<int> CurrentTickKey = new("TinyTown.CurrentTick");
    private static readonly BbKey<bool> DisableSocialActionsKey = new("TinyTown.DisableSocialActions");
    private static readonly BbKey<string> DialogueTextKey = new("TinyTown.Dialogue.Text");

    public static TinyTownSimulationResult Run(int ticks = 100, TextWriter? output = null)
        => RunScenario(ticks, null, output);

    public static TinyTownSimulationResult RunScenario(int ticks, TinyTownScenarioOptions? options = null, TextWriter? output = null)
    {
        if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));

        var profiles = CreateProfiles();
        var profilesById = profiles.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var host = new ActuatorHost();
        var fakeLlm = new ScriptedTinyTownLlmClient(options?.ForcedDialogueResponseJson);
        host.Register(new LlmTextActuationHandler(fakeLlm, new InMemoryLlmCassette(), LlmCassetteMode.Live));
        var world = new AiWorld(host);
        var eventLog = new List<string>();
        var dialogueLines = new List<string>();
        var agents = new Dictionary<string, AiAgent>(StringComparer.Ordinal);
        var ids = new Dictionary<string, AgentId>(StringComparer.Ordinal);
        var visitCursors = new Dictionary<string, EventCursor>(StringComparer.Ordinal);
        var relationships = CreateInitialRelationships();
        var memories = CreateInitialMemories();
        var dialogueOutcomes = new List<DialogueSceneOutcome>();

        foreach (var profile in profiles)
        {
            var agent = CreateTownieAgent();
            SeedTownie(agent, profile, options);
            world.Add(agent);
            agents.Add(profile.Id, agent);
            ids.Add(profile.Id, agent.Id);
            visitCursors.Add(profile.Id, default);
        }

        for (var tick = options?.StartTick ?? 0; tick < (options?.StartTick ?? 0) + ticks; tick++)
        {
            world.Bb.Set(CurrentTickKey, tick);
            foreach (var (id, agent) in agents)
            {
                TickNeeds(agent);
                var cursor = visitCursors[id];
                ConsumeVisitRequests(agent, profilesById[id], tick, eventLog, ref cursor);
                visitCursors[id] = cursor;
            }

            world.Tick(1f);
            world.Tick(1f);

            var chattedPairs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var profile in profiles)
            {
                var agent = agents[profile.Id];
                var action = agent.Bb.GetOrDefault(CurrentActionKey, "Idle");
                ExecuteAction(world, agent, profile, profilesById, ids, tick, action, eventLog, dialogueLines, relationships, memories, dialogueOutcomes, chattedPairs);
            }
        }

        var result = new TinyTownSimulationResult
        {
            TicksRun = ticks,
            FinalTownies = profiles.Select(p => Snapshot(p, agents[p.Id])).ToArray(),
            EventLog = eventLog.ToArray(),
            DialogueLines = dialogueLines.ToArray(),
            LlmCallCount = fakeLlm.CallCount,
            UsedAiDecide = agents.Values.Any(a => a.Bb.GetOrDefault(UsedAiDecideKey, false)),
            Relationships = relationships.Values.Select(ToSnapshot).OrderBy(x => RelationshipKey(x.A, x.B), StringComparer.Ordinal).ToArray(),
            Memories = memories.ToArray(),
            DialogueOutcomes = dialogueOutcomes.ToArray(),
            LlmCallContexts = fakeLlm.CanonicalContexts.ToArray()
        };

        if (output is not null)
        {
            output.WriteLine("TinyTown: LLMs are actors; utility AI is the director.");
            output.WriteLine($"Simulated {ticks} deterministic ticks for {profiles.Count} townies.");
        }

        return result;
    }

    public static TinyTownSimulationResult RunHungryScenario()
        => RunScenario(1, new TinyTownScenarioOptions
        {
            Hunger = new Dictionary<string, float> { ["maya"] = 0.05f },
            Energy = HighAll(), Social = HighAll(), Fun = HighAll(), Hygiene = HighAll(), Bladder = HighAll(),
            DisableSocialActions = true
        });

    public static TinyTownSimulationResult RunTiredScenario()
        => RunScenario(1, new TinyTownScenarioOptions
        {
            Energy = new Dictionary<string, float> { ["maya"] = 0.05f },
            Hunger = HighAll(), Social = HighAll(), Fun = HighAll(), Hygiene = HighAll(), Bladder = HighAll(),
            DisableSocialActions = true
        });

    public static TinyTownSimulationResult RunWorkScenario()
        => RunScenario(1, new TinyTownScenarioOptions
        {
            StartTick = 10,
            Hunger = HighAll(), Energy = HighAll(), Social = HighAll(), Fun = HighAll(), Hygiene = HighAll(), Bladder = HighAll(),
            Locations = new Dictionary<string, string> { ["maya"] = "MayaHome" },
            DisableSocialActions = true
        });

    public static TinyTownSimulationResult RunSocialScenario()
        => RunAwkwardMayaTheoConversation();

    public static TinyTownSimulationResult RunAwkwardMayaTheoConversation()
        => RunScenario(1, new TinyTownScenarioOptions
        {
            Hunger = HighAll(), Energy = HighAll(), Fun = HighAll(), Hygiene = HighAll(), Bladder = HighAll(),
            Social = new Dictionary<string, float> { ["maya"] = 0.1f, ["theo"] = 0.95f, ["lina"] = 0.95f, ["nia"] = 0.95f },
            Locations = new Dictionary<string, string> { ["maya"] = Cafe, ["theo"] = Cafe }
        });

    public static TinyTownSimulationResult RunFriendlyTheoNiaConversation()
        => RunScenario(1, new TinyTownScenarioOptions
        {
            Hunger = HighAll(), Energy = HighAll(), Fun = HighAll(), Hygiene = HighAll(), Bladder = HighAll(),
            Social = new Dictionary<string, float> { ["maya"] = 0.95f, ["theo"] = 0.1f, ["lina"] = 0.95f, ["nia"] = 0.95f },
            Locations = new Dictionary<string, string> { ["theo"] = Cafe, ["nia"] = Cafe }
        });

    public static TinyTownSimulationResult RunInvalidDeltaClampScenario()
        => RunScenario(1, new TinyTownScenarioOptions
        {
            Hunger = HighAll(), Energy = HighAll(), Fun = HighAll(), Hygiene = HighAll(), Bladder = HighAll(),
            Social = new Dictionary<string, float> { ["maya"] = 0.1f, ["theo"] = 0.95f, ["lina"] = 0.95f, ["nia"] = 0.95f },
            Locations = new Dictionary<string, string> { ["maya"] = Cafe, ["theo"] = Cafe },
            ForcedDialogueResponseJson = """{ "dialogue": "Maya: This is a bounded test.\nTheo: The engine decides the state.", "tone": "volatile", "outcome": "clamp_test", "affinityDelta": 999, "tensionDelta": -999, "memorySummary": "Maya and Theo tested whether impossible relationship deltas are safely clamped." }"""
        });

    private static Dictionary<string, float> HighAll() => new(StringComparer.Ordinal)
    {
        ["maya"] = 0.95f, ["theo"] = 0.95f, ["lina"] = 0.95f, ["nia"] = 0.95f
    };

    private static IReadOnlyList<TownieProfile> CreateProfiles() =>
    [
        new("maya", "Maya", "Engineer", "MayaHome", "Work", ["theo", "lina"], new TownieTraits(Social: true, HardWorker: true), new WorkSchedule(9, 17)),
        new("theo", "Theo", "Barista", "TheoHome", "Work", ["maya", "nia"], new TownieTraits(Social: true, Playful: true), new WorkSchedule(7, 15)),
        new("lina", "Lina", "Artist", "LinaHome", "Work", ["maya"], new TownieTraits(Introvert: true, Creative: true), new WorkSchedule(11, 19)),
        new("nia", "Nia", "Clerk", "NiaHome", "Work", ["theo"], new TownieTraits(Neat: true, Serious: true), new WorkSchedule(8, 16))
    ];

    private static AiAgent CreateTownieAgent()
    {
        var graph = new HfsmGraph { Root = "Root" };
        graph.Add(new HfsmStateDef { Id = "Root", Node = DecideNode });
        foreach (var action in ActionIds)
        {
            var local = action;
            graph.Add(new HfsmStateDef { Id = local, Node = ctx => ActionNode(ctx, local) });
        }

        return new AiAgent(new HfsmInstance(graph, new HfsmOptions { KeepRootFrame = true }));
    }

    private static readonly string[] ActionIds = ["UseBathroom", "Eat", "Sleep", "Shower", "GoToWork", "HaveFun", "VisitFriend", "Chat", "Idle"];

    private static IEnumerator<AiStep> DecideNode(AiCtx ctx)
    {
        while (true)
        {
            ctx.Bb.Set(UsedAiDecideKey, true);
            var profile = ProfileFrom(ctx.Bb.GetOrDefault(ProfileIdKey, "maya"));
            yield return Ai.Decide(new DecisionSlot($"TinyTown.{profile.Id}.NextAction"),
            [
                Ai.Option("UseBathroom", NeedUrgency(BladderKey), "UseBathroom"),
                Ai.Option("Eat", NeedUrgency(HungerKey), "Eat"),
                Ai.Option("Sleep", NeedUrgency(EnergyKey), "Sleep"),
                Ai.Option("Shower", NeedUrgency(HygieneKey), "Shower"),
                Ai.Option("GoToWork", new Consideration((w, a) => ScoreWork(w, a, profile)), "GoToWork"),
                Ai.Option("HaveFun", NeedUrgency(FunKey), "HaveFun"),
                Ai.Option("VisitFriend", new Consideration((w, a) => ScoreVisitFriend(a, profile)), "VisitFriend"),
                Ai.Option("Chat", new Consideration((w, a) => ScoreChat(w, a, profile)), "Chat"),
                Ai.Option("Idle", Consideration.Constant(0.05f), "Idle")
            ], hysteresis: 0.02f, minCommitSeconds: 0f, tieEpsilon: 0.0001f);
        }
    }

    private static IEnumerator<AiStep> ActionNode(AiCtx ctx, string action)
    {
        while (true)
        {
            ctx.Bb.Set(CurrentActionKey, action);
            yield return Ai.Steady(action);
        }
    }

    private static Consideration NeedUrgency(BbKey<float> key) => new((_, a) => 1f - a.Bb.GetOrDefault(key, 1f));

    private static float ScoreWork(AiWorld world, AiAgent agent, TownieProfile profile)
    {
        var tick = world.Bb.GetOrDefault(CurrentTickKey, 0) % 24;
        if (tick < profile.WorkSchedule.StartTick || tick >= profile.WorkSchedule.EndTick) return 0f;
        if (agent.Bb.GetOrDefault(LocationKey, profile.HomeLocation) == profile.WorkLocation) return 0.05f;
        return profile.Traits.HardWorker ? 0.92f : 0.82f;
    }

    private static float ScoreVisitFriend(AiAgent agent, TownieProfile profile)
    {
        if (agent.Bb.GetOrDefault(DisableSocialActionsKey, false) || profile.FriendIds.Count == 0) return 0f;
        var baseScore = 1f - agent.Bb.GetOrDefault(SocialKey, 1f);
        if (profile.Traits.Introvert) baseScore *= 0.55f;
        if (profile.Traits.Social) baseScore *= 1.15f;
        return Math.Clamp(baseScore, 0f, 0.78f);
    }

    private static float ScoreChat(AiWorld world, AiAgent agent, TownieProfile profile)
    {
        if (agent.Bb.GetOrDefault(DisableSocialActionsKey, false)) return 0f;
        var location = agent.Bb.GetOrDefault(LocationKey, profile.HomeLocation);
        foreach (var other in world.Agents)
        {
            if (ReferenceEquals(other, agent)) continue;
            var otherId = other.Bb.GetOrDefault(ProfileIdKey, string.Empty);
            if (!profile.FriendIds.Contains(otherId, StringComparer.Ordinal)) continue;
            if (other.Bb.GetOrDefault(LocationKey, string.Empty) == location)
            {
                var socialNeed = 1f - agent.Bb.GetOrDefault(SocialKey, 1f);
                return Math.Clamp(socialNeed + 0.25f, 0f, 0.95f);
            }
        }
        return 0f;
    }

    private static void SeedTownie(AiAgent agent, TownieProfile profile, TinyTownScenarioOptions? options)
    {
        agent.Bb.Set(ProfileIdKey, profile.Id);
        agent.Bb.Set(LocationKey, Get(options?.Locations, profile.Id, profile.HomeLocation));
        agent.Bb.Set(CurrentActionKey, "Idle");
        agent.Bb.Set(HungerKey, Get(options?.Hunger, profile.Id, 0.72f));
        agent.Bb.Set(EnergyKey, Get(options?.Energy, profile.Id, 0.78f));
        agent.Bb.Set(SocialKey, Get(options?.Social, profile.Id, 0.70f));
        agent.Bb.Set(FunKey, Get(options?.Fun, profile.Id, 0.68f));
        agent.Bb.Set(HygieneKey, Get(options?.Hygiene, profile.Id, 0.80f));
        agent.Bb.Set(BladderKey, Get(options?.Bladder, profile.Id, 0.82f));
        agent.Bb.Set(DisableSocialActionsKey, options?.DisableSocialActions ?? false);
    }

    private static float Get(IReadOnlyDictionary<string, float>? values, string key, float fallback)
        => values is not null && values.TryGetValue(key, out var value) ? Clamp(value) : fallback;

    private static string Get(IReadOnlyDictionary<string, string>? values, string key, string fallback)
        => values is not null && values.TryGetValue(key, out var value) ? value : fallback;

    private static void TickNeeds(AiAgent agent)
    {
        Dec(agent, HungerKey, 0.015f);
        Dec(agent, EnergyKey, 0.012f);
        Dec(agent, SocialKey, 0.012f);
        Dec(agent, FunKey, 0.011f);
        Dec(agent, HygieneKey, 0.009f);
        Dec(agent, BladderKey, 0.025f);
    }

    private static void ExecuteAction(
        AiWorld world,
        AiAgent agent,
        TownieProfile profile,
        IReadOnlyDictionary<string, TownieProfile> profilesById,
        IReadOnlyDictionary<string, AgentId> ids,
        int tick,
        string action,
        List<string> eventLog,
        List<string> dialogueLines,
        Dictionary<string, RelationshipState> relationships,
        List<TownMemoryRecord> memories,
        List<DialogueSceneOutcome> dialogueOutcomes,
        HashSet<string> chattedPairs)
    {
        switch (action)
        {
            case "UseBathroom": Inc(agent, BladderKey, 0.45f); eventLog.Add($"tick {tick}: {profile.Name} used bathroom"); break;
            case "Eat": Inc(agent, HungerKey, 0.38f); eventLog.Add($"tick {tick}: {profile.Name} ate at {agent.Bb.GetOrDefault(LocationKey, profile.HomeLocation)}"); break;
            case "Sleep": Inc(agent, EnergyKey, 0.42f); eventLog.Add($"tick {tick}: {profile.Name} slept"); break;
            case "Shower": Inc(agent, HygieneKey, 0.44f); eventLog.Add($"tick {tick}: {profile.Name} showered"); break;
            case "HaveFun": Inc(agent, FunKey, 0.35f); eventLog.Add($"tick {tick}: {profile.Name} had fun at the Park"); agent.Bb.Set(LocationKey, Park); break;
            case "GoToWork":
                agent.Bb.Set(LocationKey, profile.WorkLocation);
                Dec(agent, EnergyKey, 0.02f); Dec(agent, FunKey, 0.02f);
                eventLog.Add($"tick {tick}: {profile.Name} went to Work as {profile.Job}");
                break;
            case "VisitFriend": ExecuteVisit(world, agent, profile, ids, tick, eventLog); break;
            case "Chat": ExecuteChat(world, agent, profile, profilesById, tick, eventLog, dialogueLines, relationships, memories, dialogueOutcomes, chattedPairs); break;
            default: eventLog.Add($"tick {tick}: {profile.Name} idled"); break;
        }
    }

    private static void ExecuteVisit(AiWorld world, AiAgent agent, TownieProfile profile, IReadOnlyDictionary<string, AgentId> ids, int tick, List<string> eventLog)
    {
        var friendId = profile.FriendIds[0];
        var location = profile.Traits.Introvert ? Park : Cafe;
        agent.Bb.Set(LocationKey, location);
        Inc(agent, SocialKey, 0.08f);
        if (ids.TryGetValue(friendId, out var to))
        {
            world.Mail.Send(to, new FriendVisitRequested(profile.Id, friendId, location, tick));
            eventLog.Add($"tick {tick}: {profile.Name} requested visit with {NameOf(friendId)} at {location}");
        }
    }

    private static void ConsumeVisitRequests(AiAgent agent, TownieProfile profile, int tick, List<string> eventLog, ref EventCursor cursor)
    {
        if (!agent.Events.TryConsume(ref cursor, (FriendVisitRequested e) => e.ToTownieId == profile.Id, out var request)) return;
        agent.Bb.Set(LocationKey, request.Location);
        Inc(agent, SocialKey, 0.05f);
        eventLog.Add($"tick {tick}: {profile.Name} received visit request from {NameOf(request.FromTownieId)} at {request.Location}");
    }

    private static void ExecuteChat(
        AiWorld world,
        AiAgent agent,
        TownieProfile profile,
        IReadOnlyDictionary<string, TownieProfile> profilesById,
        int tick,
        List<string> eventLog,
        List<string> dialogueLines,
        Dictionary<string, RelationshipState> relationships,
        List<TownMemoryRecord> memories,
        List<DialogueSceneOutcome> dialogueOutcomes,
        HashSet<string> chattedPairs)
    {
        var location = agent.Bb.GetOrDefault(LocationKey, profile.HomeLocation);
        var friend = world.Agents.FirstOrDefault(other => !ReferenceEquals(other, agent)
            && other.Bb.GetOrDefault(LocationKey, string.Empty) == location
            && profile.FriendIds.Contains(other.Bb.GetOrDefault(ProfileIdKey, string.Empty), StringComparer.Ordinal));
        if (friend is null) { eventLog.Add($"tick {tick}: {profile.Name} wanted to chat but no friend was nearby"); return; }

        var friendProfile = profilesById[friend.Bb.GetOrDefault(ProfileIdKey, string.Empty)];
        var relationshipKey = RelationshipKey(profile.Id, friendProfile.Id);
        if (!chattedPairs.Add(relationshipKey))
        {
            eventLog.Add($"tick {tick}: {profile.Name} deferred duplicate Chat with {friendProfile.Name} at {location}");
            return;
        }

        Inc(agent, SocialKey, 0.28f);
        Inc(friend, SocialKey, 0.18f);

        var before = relationships[relationshipKey];
        var relevantMemories = RelevantMemories(memories, profile.Id, friendProfile.Id);
        var rawJson = GenerateDialogueJson(world, agent, friend, profile, friendProfile, before, relevantMemories, location, tick);
        var outcome = ValidateDialogueOutcome(ParseDialogueOutcome(rawJson));
        var committed = before with
        {
            Affinity = Clamp(before.Affinity + outcome.AffinityDelta),
            Tension = Clamp(before.Tension + outcome.TensionDelta),
            LastInteractionTick = tick
        };
        relationships[relationshipKey] = committed;

        var memoryId = $"memory.{relationshipKey.Replace(':', '-')}.chat.{tick}";
        memories.Add(new TownMemoryRecord
        {
            Id = memoryId,
            Tick = tick,
            TownieIds = [profile.Id, friendProfile.Id],
            Kind = "dialogue",
            Summary = outcome.MemorySummary
        });

        dialogueOutcomes.Add(outcome);
        dialogueLines.Add(outcome.Dialogue);
        eventLog.Add($"tick {tick}: {profile.Name} Chat with {friendProfile.Name} at {location}: {outcome.Dialogue}");
        eventLog.Add($"tick {tick}: DM scene outcome {outcome.Outcome} tone {outcome.Tone}");
        eventLog.Add($"tick {tick}: Relationship {relationshipKey} affinity {outcome.AffinityDelta:+0.00;-0.00;+0.00} tension {outcome.TensionDelta:+0.00;-0.00;+0.00}");
        eventLog.Add($"tick {tick}: Memory recorded {memoryId}");
    }

    private static string GenerateDialogueJson(
        AiWorld world,
        AiAgent speakerAgent,
        AiAgent listenerAgent,
        TownieProfile speaker,
        TownieProfile listener,
        RelationshipState relationship,
        IReadOnlyList<TownMemoryRecord> relevantMemories,
        string location,
        int tick)
    {
        var step = global::Dominatus.Llm.OptFlow.Llm.Call(
            stableId: $"tinytown.dialogue.{speaker.Id}.{listener.Id}.{tick}",
            intent: "Simulate a short DM-adjudicated townie conversation and return structured scene outcome JSON with dialogue, tone, outcome, affinityDelta, tensionDelta, and memorySummary.",
            persona: "Bounded life-sim DM. Narrate the scene and propose social consequences only; the engine commits state.",
            context: b =>
            {
                b.Add("speaker", speaker.Name);
                b.Add("listener", listener.Name);
                b.Add("location", location);
                b.Add("speaker_profile", ProfileSummary(speaker));
                b.Add("listener_profile", ProfileSummary(listener));
                b.Add("speaker_needs", NeedsSummary(speakerAgent));
                b.Add("listener_needs", NeedsSummary(listenerAgent));
                b.Add("relationship", $"key={RelationshipKey(speaker.Id, listener.Id)}; affinity={relationship.Affinity:0.00}; tension={relationship.Tension:0.00}; unresolvedIssueId={relationship.UnresolvedIssueId ?? "none"}");
                b.Add("relevant_memories", relevantMemories.Count == 0 ? "none" : string.Join(" | ", relevantMemories.Select(m => $"{m.Id}: {m.Summary}")));
                b.Add("task", "Return only JSON for DialogueSceneOutcome. Deltas are suggestions and must stay within -0.25..0.25; Dominatus validates and commits state.");
            },
            storeTextAs: DialogueTextKey);

        var ctx = new AiCtx(world, speakerAgent, speakerAgent.Events, default, world.View, world.Mail, world.Actuator);
        var wait = (IWaitEvent)step;
        var cursor = default(EventCursor);
        if (!wait.TryConsume(ctx, ref cursor)) wait.TryConsume(ctx, ref cursor);
        return speakerAgent.Bb.GetOrDefault(DialogueTextKey, string.Empty);
    }

    private static Dictionary<string, RelationshipState> CreateInitialRelationships()
    {
        var relationships = new Dictionary<string, RelationshipState>(StringComparer.Ordinal);
        AddRelationship(relationships, "maya", "theo", 0.65f, 0.45f, "missed-celebration");
        AddRelationship(relationships, "maya", "lina", 0.75f, 0.10f, null);
        AddRelationship(relationships, "theo", "nia", 0.70f, 0.15f, null);
        return relationships;
    }

    private static void AddRelationship(Dictionary<string, RelationshipState> relationships, string a, string b, float affinity, float tension, string? unresolvedIssueId)
    {
        var (first, second) = OrderedPair(a, b);
        relationships.Add(RelationshipKey(first, second), new RelationshipState
        {
            A = first,
            B = second,
            Affinity = Clamp(affinity),
            Tension = Clamp(tension),
            LastInteractionTick = -1,
            UnresolvedIssueId = unresolvedIssueId
        });
    }

    private static List<TownMemoryRecord> CreateInitialMemories() =>
    [
        new TownMemoryRecord
        {
            Id = "memory.maya-theo.missed-celebration",
            Tick = -48,
            TownieIds = ["maya", "theo"],
            Kind = "conflict",
            Summary = "Theo missed Maya's work celebration after promising to come. Maya felt ignored, and Theo avoided the subject afterward."
        }
    ];

    private static IReadOnlyList<TownMemoryRecord> RelevantMemories(IEnumerable<TownMemoryRecord> memories, string a, string b)
        => memories.Where(m => m.TownieIds.Contains(a, StringComparer.Ordinal) && m.TownieIds.Contains(b, StringComparer.Ordinal)).ToArray();

    private static DialogueSceneOutcome ParseDialogueOutcome(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DialogueSceneOutcome>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? EmptyDialogueOutcome();
        }
        catch (JsonException)
        {
            return EmptyDialogueOutcome();
        }
    }

    private static DialogueSceneOutcome ValidateDialogueOutcome(DialogueSceneOutcome outcome) => new()
    {
        Dialogue = RequiredBounded(outcome.Dialogue, 2_000, "The conversation could not be narrated."),
        Tone = RequiredBounded(outcome.Tone, 64, "unknown"),
        Outcome = RequiredBounded(outcome.Outcome, 64, "unresolved"),
        AffinityDelta = ClampDelta(outcome.AffinityDelta),
        TensionDelta = ClampDelta(outcome.TensionDelta),
        MemorySummary = RequiredBounded(outcome.MemorySummary, 512, "The conversation happened, but no reliable summary was provided.")
    };

    private static DialogueSceneOutcome EmptyDialogueOutcome() => new()
    {
        Dialogue = string.Empty,
        Tone = string.Empty,
        Outcome = string.Empty,
        MemorySummary = string.Empty
    };

    private static string RequiredBounded(string? value, int maxLength, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static float ClampDelta(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
        return Math.Clamp(value, -0.25f, 0.25f);
    }

    private static RelationshipSnapshot ToSnapshot(RelationshipState relationship) => new()
    {
        A = relationship.A,
        B = relationship.B,
        Affinity = relationship.Affinity,
        Tension = relationship.Tension,
        LastInteractionTick = relationship.LastInteractionTick,
        UnresolvedIssueId = relationship.UnresolvedIssueId
    };

    private static string RelationshipKey(string a, string b)
    {
        var (first, second) = OrderedPair(a, b);
        return $"{first}:{second}";
    }

    private static (string First, string Second) OrderedPair(string a, string b)
        => string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

    private static string ProfileSummary(TownieProfile profile)
        => $"id={profile.Id}; name={profile.Name}; job={profile.Job}; traits={TraitsSummary(profile.Traits)}";

    private static string TraitsSummary(TownieTraits traits)
    {
        var values = new List<string>();
        if (traits.Social) values.Add("Social");
        if (traits.Introvert) values.Add("Introvert");
        if (traits.HardWorker) values.Add("HardWorker");
        if (traits.Playful) values.Add("Playful");
        if (traits.Creative) values.Add("Creative");
        if (traits.Neat) values.Add("Neat");
        if (traits.Serious) values.Add("Serious");
        return values.Count == 0 ? "none" : string.Join(",", values);
    }

    private static string NeedsSummary(AiAgent agent)
        => $"hunger={agent.Bb.GetOrDefault(HungerKey, 1f):0.00}; energy={agent.Bb.GetOrDefault(EnergyKey, 1f):0.00}; social={agent.Bb.GetOrDefault(SocialKey, 1f):0.00}; fun={agent.Bb.GetOrDefault(FunKey, 1f):0.00}; hygiene={agent.Bb.GetOrDefault(HygieneKey, 1f):0.00}; bladder={agent.Bb.GetOrDefault(BladderKey, 1f):0.00}";

    private static TownieSnapshot Snapshot(TownieProfile profile, AiAgent agent) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Location = agent.Bb.GetOrDefault(LocationKey, profile.HomeLocation),
        CurrentAction = agent.Bb.GetOrDefault(CurrentActionKey, "Idle"),
        Hunger = agent.Bb.GetOrDefault(HungerKey, 0f),
        Energy = agent.Bb.GetOrDefault(EnergyKey, 0f),
        Social = agent.Bb.GetOrDefault(SocialKey, 0f),
        Fun = agent.Bb.GetOrDefault(FunKey, 0f),
        Hygiene = agent.Bb.GetOrDefault(HygieneKey, 0f),
        Bladder = agent.Bb.GetOrDefault(BladderKey, 0f)
    };

    private static void Inc(AiAgent agent, BbKey<float> key, float amount) => agent.Bb.Set(key, Clamp(agent.Bb.GetOrDefault(key, 0f) + amount));
    private static void Dec(AiAgent agent, BbKey<float> key, float amount) => agent.Bb.Set(key, Clamp(agent.Bb.GetOrDefault(key, 0f) - amount));
    private static float Clamp(float value) => Math.Clamp(value, 0f, 1f);
    private static TownieProfile ProfileFrom(string id) => CreateProfiles().First(p => p.Id == id);
    private static string NameOf(string id) => CreateProfiles().First(p => p.Id == id).Name;

    private sealed class ScriptedTinyTownLlmClient : ILlmClient
    {
        private readonly string? _forcedResponseJson;
        public int CallCount { get; private set; }
        public List<string> CanonicalContexts { get; } = [];

        public ScriptedTinyTownLlmClient(string? forcedResponseJson = null)
        {
            _forcedResponseJson = forcedResponseJson;
        }

        public Task<LlmTextResult> GenerateTextAsync(LlmTextRequest request, string requestHash, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            CanonicalContexts.Add(request.CanonicalContextJson);

            var text = _forcedResponseJson
                ?? (IsPair(request.StableId, "maya", "theo") ? AwkwardMayaTheoJson() : GenericFriendlyJson(request.StableId));
            return Task.FromResult(new LlmTextResult(text, requestHash, request.Sampling.Provider, request.Sampling.Model));
        }

        private static bool IsPair(string stableId, string a, string b)
        {
            var parts = stableId.Split('.');
            return parts.Length >= 5
                && ((string.Equals(parts[2], a, StringComparison.Ordinal) && string.Equals(parts[3], b, StringComparison.Ordinal))
                    || (string.Equals(parts[2], b, StringComparison.Ordinal) && string.Equals(parts[3], a, StringComparison.Ordinal)));
        }

        private static string AwkwardMayaTheoJson() => """
            {
              "dialogue": "Maya: So... are we pretending you didn’t miss my celebration?\nTheo: I wasn’t pretending. I just didn’t know how to apologize.",
              "tone": "awkward",
              "outcome": "partial_repair",
              "affinityDelta": 0.08,
              "tensionDelta": -0.12,
              "memorySummary": "Maya and Theo had an awkward but honest conversation about Theo missing her work celebration. The tension eased slightly, but the issue is not fully resolved."
            }
            """;

        private static string GenericFriendlyJson(string stableId)
        {
            var parts = stableId.Split('.');
            var speaker = parts.Length >= 5 ? NameOf(parts[2]) : "Theo";
            var listener = parts.Length >= 5 ? NameOf(parts[3]) : "Nia";
            return $$"""
                {
                  "dialogue": "{{speaker}}: Good to see you. The cafe feels quieter today.\n{{listener}}: Quiet is underrated.",
                  "tone": "warm",
                  "outcome": "friendly_chat",
                  "affinityDelta": 0.04,
                  "tensionDelta": -0.02,
                  "memorySummary": "{{speaker}} and {{listener}} shared a brief friendly chat at the cafe."
                }
                """;
        }
    }
}