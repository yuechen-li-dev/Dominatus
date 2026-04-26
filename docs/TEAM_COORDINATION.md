# Team Coordination

Dominatus does not currently provide a native writable team/squad blackboard. Team coordination should use the **lead/owner-agent pattern**: one lead agent owns durable team memory in its own `ctx.Bb`, members communicate with the lead through mailbox messages (requests/events/proposals), and truly global facts belong in `ctx.WorldBb`.

This keeps team coordination deterministic and parallel-friendly without introducing shared mutable writes between peer agents.

## The three blackboard scopes

Dominatus currently has three practical memory scopes:

- **`ctx.Bb`**: agent-local durable state owned by a single agent.
- **`ctx.WorldBb`**: shared world/session durable state for facts that are truly global to the `AiWorld`.
- **Team memory (recommended pattern)**: lead-agent-owned durable state stored in one designated agent's `ctx.Bb`.

`ctx.WorldBb` is intentionally broad and should not become every squad's scratchpad. If a fact is specific to one party/squad, prefer lead-owned team memory.

## Why not native team blackboards?

Dominatus treats agents as conceptually independent mutable units.

- Agent-local writes are safe because each agent exclusively owns its `ctx.Bb`.
- A writable native team blackboard would allow multiple agents to write to one shared mutable object in the same tick.
- Shared writes force runtime-level concurrency policy questions: locks, staged writes, deterministic conflict resolution, or strict single-thread/single-writer assumptions.
- Dominatus should not take on that complexity until a concrete, proven use case requires it.

Reads are not the problem; **shared writes** are the problem.

## Recommended pattern: squad lead owns team memory

Use one agent as the team/squad lead.

- The lead stores durable team facts in its own `ctx.Bb` (for example: objective, priority target, role assignments, claimed resources, searched rooms, formation anchor).
- Members send typed mailbox messages to the lead (for example: propose target, request claim, report threat, report completion).
- The lead updates its own blackboard and broadcasts relevant update events back to members.

This doctrine preserves:

- single-writer ownership for durable team memory,
- parallel-friendly local mutation,
- normal mailbox semantics,
- inspectable team state,
- no new core primitive.

## Mailbox vs team memory

Use **mailbox** for:

- events,
- requests,
- notifications,
- proposals,
- one-time signals.

Use **team memory** (lead-owned durable state) for:

- durable shared facts,
- current squad objective,
- role assignments,
- claimed resources,
- last known threat.

Do **not** use mailbox as a replicated key-value store.
Do **not** broadcast the same durable fact every tick.
If a fact should persist, it belongs in an owning agent blackboard (or in `ctx.WorldBb` if it is truly world/session-wide).

## WorldBb vs lead-owned team state

Use `ctx.WorldBb` for facts that are genuinely global across the whole `AiWorld`, such as:

- world phase,
- weather,
- economy-wide price tables,
- global scenario flags,
- server-wide budgets.

Use lead-owned team state for facts scoped to one group, such as:

- party objective,
- squad target,
- team resource claims,
- raid phase for one group,
- small-group memory.

## Example: squad lead pattern

The following snippets are illustrative and may need minor adaptation to your local helper style.

### Message/event contracts

```csharp
public sealed record ReportThreat(AgentId Reporter, AgentId Target, float Severity);
public sealed record ClaimResourceRequest(AgentId Requester, string ResourceId);
public sealed record TeamObjectiveChanged(string Objective);
```

### Lead-owned team keys

```csharp
public static class SquadKeys
{
    public static readonly BbKey<string> CurrentObjective = new("Squad.CurrentObjective");
    public static readonly BbKey<string> PriorityTarget = new("Squad.PriorityTarget");
    public static readonly BbKey<string[]> ClaimedResources = new("Squad.ClaimedResources");
}
```

### Lead node (illustrative)

```csharp
public static IEnumerator<AiStep> SquadLead(AiCtx ctx)
{
    while (true)
    {
        // Illustrative WaitEvent<T>(out T) style shown in architecture docs and examples.
        // If your local API differs, adapt to that signature.
        yield return Ai.WaitEvent<ReportThreat>(out var threat);

        ctx.Bb.Set(SquadKeys.PriorityTarget, threat.Target.ToString());
        ctx.Bb.Set(SquadKeys.CurrentObjective, "FocusTarget");

        ctx.Mail.Broadcast(
            s => s.Team == ctx.View.GetAgent(ctx.Agent.Id).Team,
            new TeamObjectiveChanged("FocusTarget"));
    }
}
```

Here `s.Team` is public grouping metadata from `AgentSnapshot`, not a mutable team blackboard.

### Member node (illustrative)

```csharp
public static IEnumerator<AiStep> SquadMember(AiCtx ctx, AgentId leadId, AgentId targetId)
{
    ctx.Mail.Send(leadId, new ReportThreat(ctx.Agent.Id, targetId, 0.9f));
    yield return Ai.Wait(0.25f);
}
```

## Relationship to `AgentSnapshot.Team`

Public snapshots include a `Team` value for grouping/filtering.

- Use `AgentSnapshot.Team` as public metadata.
- Use `World.View.QueryAgents(...)` to locate peers by team.
- Do not confuse `AgentSnapshot.Team` with mutable shared team memory.

## Future native team state

Native team-scoped state may become justified later if all of the following are designed intentionally:

- a real use case needs high-performance read-mostly team facts,
- staged writes and deterministic conflict policy are defined,
- parallel agent execution semantics for team writes are explicit.

A future design could use:

- read-only visible team snapshot during ticks,
- staged writes committed at end of tick,
- single-writer/owner policy,
- deterministic conflict resolution.

**Not implemented today.**

## Rules of thumb

1. Use `ctx.Bb` for private agent state.
2. Use `ctx.WorldBb` for true world/session state.
3. Use mailbox for events, requests, and proposals.
4. Use a lead/owner agent's `ctx.Bb` for team memory.
5. Avoid shared writable state between peer agents.
6. Do not broadcast durable facts every tick.
7. If multiple agents must negotiate, model it as messages.
8. If only one agent should decide, make that agent the owner.
9. If all agents need to read it, publish updates explicitly.
10. Do not add native writable team blackboards until parallel write semantics are designed.
