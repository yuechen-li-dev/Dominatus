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

public sealed record TinyTownSimulationResult
{
    public required int TicksRun { get; init; }
    public required IReadOnlyList<TownieSnapshot> FinalTownies { get; init; }
    public required IReadOnlyList<string> EventLog { get; init; }
    public required IReadOnlyList<string> DialogueLines { get; init; }
    public required int LlmCallCount { get; init; }
    public required bool UsedAiDecide { get; init; }
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
        var fakeLlm = new ScriptedTinyTownLlmClient();
        host.Register(new LlmTextActuationHandler(fakeLlm, new InMemoryLlmCassette(), LlmCassetteMode.Live));
        var world = new AiWorld(host);
        var eventLog = new List<string>();
        var dialogueLines = new List<string>();
        var agents = new Dictionary<string, AiAgent>(StringComparer.Ordinal);
        var ids = new Dictionary<string, AgentId>(StringComparer.Ordinal);
        var visitCursors = new Dictionary<string, EventCursor>(StringComparer.Ordinal);

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

            foreach (var profile in profiles)
            {
                var agent = agents[profile.Id];
                var action = agent.Bb.GetOrDefault(CurrentActionKey, "Idle");
                ExecuteAction(world, agent, profile, profilesById, ids, tick, action, eventLog, dialogueLines);
            }
        }

        var result = new TinyTownSimulationResult
        {
            TicksRun = ticks,
            FinalTownies = profiles.Select(p => Snapshot(p, agents[p.Id])).ToArray(),
            EventLog = eventLog.ToArray(),
            DialogueLines = dialogueLines.ToArray(),
            LlmCallCount = fakeLlm.CallCount,
            UsedAiDecide = agents.Values.Any(a => a.Bb.GetOrDefault(UsedAiDecideKey, false))
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
        => RunScenario(4, new TinyTownScenarioOptions
        {
            Hunger = HighAll(), Energy = HighAll(), Fun = HighAll(), Hygiene = HighAll(), Bladder = HighAll(),
            Social = new Dictionary<string, float> { ["maya"] = 0.1f, ["theo"] = 0.1f },
            Locations = new Dictionary<string, string> { ["maya"] = Cafe, ["theo"] = Cafe }
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

    private static void ExecuteAction(AiWorld world, AiAgent agent, TownieProfile profile, IReadOnlyDictionary<string, TownieProfile> profilesById, IReadOnlyDictionary<string, AgentId> ids, int tick, string action, List<string> eventLog, List<string> dialogueLines)
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
            case "Chat": ExecuteChat(world, agent, profile, profilesById, tick, eventLog, dialogueLines); break;
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

    private static void ExecuteChat(AiWorld world, AiAgent agent, TownieProfile profile, IReadOnlyDictionary<string, TownieProfile> profilesById, int tick, List<string> eventLog, List<string> dialogueLines)
    {
        var location = agent.Bb.GetOrDefault(LocationKey, profile.HomeLocation);
        var friend = world.Agents.FirstOrDefault(other => !ReferenceEquals(other, agent)
            && other.Bb.GetOrDefault(LocationKey, string.Empty) == location
            && profile.FriendIds.Contains(other.Bb.GetOrDefault(ProfileIdKey, string.Empty), StringComparer.Ordinal));
        if (friend is null) { eventLog.Add($"tick {tick}: {profile.Name} wanted to chat but no friend was nearby"); return; }
        var friendProfile = profilesById[friend.Bb.GetOrDefault(ProfileIdKey, string.Empty)];
        Inc(agent, SocialKey, 0.28f);
        Inc(friend, SocialKey, 0.18f);
        var line = GenerateDialogue(world, agent, profile, friendProfile, location, tick);
        dialogueLines.Add(line);
        eventLog.Add($"tick {tick}: {profile.Name} Chat with {friendProfile.Name} at {location}: {line}");
    }

    private static string GenerateDialogue(AiWorld world, AiAgent agent, TownieProfile speaker, TownieProfile listener, string location, int tick)
    {
        var step = global::Dominatus.Llm.OptFlow.Llm.Call(
            stableId: $"tinytown.dialogue.{speaker.Id}.{listener.Id}.{tick}",
            intent: "Generate one short friendly line of life-sim dialogue between two townies.",
            persona: "Lightweight life-sim dialogue writer.",
            context: b =>
            {
                b.Add("speaker", speaker.Name);
                b.Add("listener", listener.Name);
                b.Add("location", location);
                b.Add("need_summary", $"hunger={agent.Bb.GetOrDefault(HungerKey, 1f):0.00}; energy={agent.Bb.GetOrDefault(EnergyKey, 1f):0.00}; social={agent.Bb.GetOrDefault(SocialKey, 1f):0.00}");
                b.Add("relationship", "friends");
            },
            storeTextAs: DialogueTextKey);

        var ctx = new AiCtx(world, agent, agent.Events, default, world.View, world.Mail, world.Actuator);
        var wait = (IWaitEvent)step;
        var cursor = default(EventCursor);
        if (!wait.TryConsume(ctx, ref cursor)) wait.TryConsume(ctx, ref cursor);
        return agent.Bb.GetOrDefault(DialogueTextKey, string.Empty);
    }

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
        public int CallCount { get; private set; }

        public Task<LlmTextResult> GenerateTextAsync(LlmTextRequest request, string requestHash, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            var text = request.StableId.Contains("maya.theo", StringComparison.OrdinalIgnoreCase)
                ? "Maya: Good to see you, Theo. Work has been a lot today."
                : BuildGenericLine(request.StableId);
            return Task.FromResult(new LlmTextResult(text, requestHash, request.Sampling.Provider, request.Sampling.Model));
        }

        private static string BuildGenericLine(string stableId)
        {
            var parts = stableId.Split('.');
            if (parts.Length >= 5) return $"{NameOf(parts[2])}: Good to see you, {NameOf(parts[3])}.";
            return "Maya: Good to see you, Theo. Work has been a lot today.";
        }
    }
}
