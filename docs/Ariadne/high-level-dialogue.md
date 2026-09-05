# High-level Ariadne dialogue

Use high-level `Dialogue` for ordinary conversation authoring. It constructs immutable data, validates the complete structural graph, and lowers to a normal `FlowDefinition`. Ariadne OptFlow remains the only runtime.

```text
C# builder ─┐
TOML data ──┼→ DialogueDefinition<TConsequence> → DialogueValidator → DialogueLowerer → FlowDefinition
Copeland TS ┘
```

Only the C# builder is implemented in M7a. TOML is a future adapter to the same definition model. Copeland TS/TSX, Machina presentation, InputMan, voice, quests, and localization frameworks are not part of this layer.

## Authoring

```csharp
using Ariadne.OptFlow.Dialogue;
using Dominatus.Core.Blackboard;

var raining = new BbKey<bool>("world.raining");

DialogueDefinition<GameIntent> askHow = Dialogue.Define<GameIntent>(
    "mara.ask-how",
    dialogue =>
    {
        dialogue.Say("answer", "Mara", "I'm well enough.");
        dialogue.End();
    });

DialogueDefinition<GameIntent> morning = Dialogue.Define<GameIntent>(
    "mara.morning",
    dialogue =>
    {
        dialogue.Say("morning", "Mara", DialogueText.Key("mara.morning", "Morning."));
        dialogue.When("rain-check", DialogueCondition.IsTrue(raining), rain =>
        {
            rain.Say("rain-comment", "Mara", "Awful weather today.");
        });
        dialogue.Choice("topic", "What do you ask?", choices =>
        {
            choices.Option("ask-how", "How are you?", branch =>
            {
                branch.Call("ask-how-call", askHow);
            });
            choices.Option("offer-help", "Need any help?", branch =>
            {
                branch.Emit("offer-help", new OfferHelpIntent("Mara"));
            });
        });
        dialogue.End();
    });

DialogueLoweringResult<GameIntent> lowered = DialogueLowerer.Lower(morning);
AiAgent agent = new(lowered.Flow.CreateBrain());
```

Builder lambdas run immediately during definition construction. They are not runtime continuations and cannot capture a live session for later execution. The resulting records contain no iterator, `AiCtx`, `GameSession`, or runtime cursor.

## Semantic laws

- Every dialogue and meaningful construct has an explicit typed local ID. Displayed text and source lines are never identity.
- `Narrate` creates a line with no speaker. It does not invent a narrator entity.
- Conditions are data over explicit blackboard keys: `IsTrue`, `IsFalse`, or typed `Equals`. Arbitrary runtime predicates and closure capture are not accepted.
- `TConsequence` belongs to the application. `Emit` dispatches `DialogueEffectCommand<TConsequence>`; an application actuator handles it. Dialogue does not mutate gameplay state.
- Options preserve declaration order. An optional semantic availability condition makes an option available or hidden; disabled-visible is deferred.
- `Call` is the only high-level subdialogue operation and lowers to `Push` plus exhaustive return routing. Ordinary authoring never chooses between `Goto` and `Push`.
- `LoopTo` is the only ordinary backward edge. It visibly marks intent, and cycle validation rejects cycles without that marker.
- `End` terminates the current dialogue or branch. A default local ID of `end` is available; use an explicit ID where a dialogue contains multiple branch ends.

## Validation and registries

Call `DialogueValidator.Validate` directly for editor feedback, or call `DialogueLowerer.Lower`, which rejects an invalid definition before creating OptFlow states.

`DialogueValidationOptions` can supply known speakers and known condition-key names. The validator checks duplicate local/composite IDs, empty choices, missing calls, call recursion, invalid loop targets, dangling paths, reachability, cycle intent, durable condition types, and generated operation-ID limits.

## Typed consequence handler

```csharp
public sealed class GameDialogueEffects : IActuationHandler<DialogueEffectCommand<GameIntent>>
{
    public ActuatorHost.HandlerResult Handle(
        ActuatorHost host,
        AiCtx context,
        ActuationId id,
        DialogueEffectCommand<GameIntent> command)
    {
        gameIntentQueue.Enqueue(command.Consequence);
        return ActuatorHost.HandlerResult.CompletedOk();
    }
}
```

This boundary may enqueue or route the consequence to the application. It should not move game semantics into Ariadne.

## Choosing the level

Use high-level `Dialogue` for ordinary conversation authoring. Use raw OptFlow when you need unrestricted control-flow composition, free-text `Diag.Ask`, utility decisions, persistent event listening, or other expert behavior outside the closed dialogue graph. Raw OptFlow remains supported and is not deprecated.

