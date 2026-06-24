# Dominatus.Pay M2 — Stripe webhook verification and normalized event ingestion

M2 adds a production-shaped, still-kernel-sized webhook seam for `Dominatus.Actuators.Payments.Stripe`.

## Stripe docs facts verified

On 2026-06-24, the implementation was checked against official Stripe docs:

- Stripe recommends securing webhook endpoints by verifying the event with the exact raw request body string, the `Stripe-Signature` header, and the endpoint secret.
- Endpoint secrets shown by Stripe/Stripe CLI use the `whsec_` prefix.
- The raw request body must be the UTF-8 body Stripe sent, without whitespace changes, JSON reserialization, key reordering, or framework body-parser mutation.
- Stripe documents HTTPS for public webhook endpoints and recommends quickly returning `2xx` before long business work.
- Stripe event types include `checkout.session.completed`, `checkout.session.expired`, `payment_intent.succeeded`, `payment_intent.payment_failed`, `payment_intent.canceled`, `payment_intent.amount_capturable_updated`, `charge.refunded`, `refund.created`, and `refund.updated`.
- Stripe.net exposes official webhook signature helpers; M2 uses `EventUtility.ValidateSignature` before parsing and mapping.

## Implemented API

`StripeWebhookVerifier.VerifyAndNormalizeAsync(rawBody, stripeSignatureHeader, options, cancellationToken)` returns a safe `StripeWebhookProcessingResult` instead of requiring endpoint code to handle provider exceptions directly.

Required inputs:

- `rawBody`: exact raw Stripe request body string.
- `stripeSignatureHeader`: the full `Stripe-Signature` header value from the request.
- `StripeWebhookOptions.EndpointSecret`: the webhook endpoint secret. It is required and must not be logged.

Validation and failure behavior:

- missing endpoint secret, missing signature, or missing raw body returns `Verified = false` with a sanitized `SafeError`;
- signature failures return `Verified = false` and do not include endpoint secrets, API keys, client secrets, raw payloads, or full signature values;
- JSON parsing happens only after signature validation;
- unknown verified event types are not signature failures.

Raw provider event JSON is excluded by default. If `IncludeRawProviderEventJson` is explicitly true, `RawProviderEventJson` is capped by `MaxRawEventJsonChars`. Prefer leaving it off in production logs/storage.

## Normalized payment event model

The base payments package now exposes provider-neutral event ingestion primitives:

- `PaymentEventEnvelope`
- `PaymentNormalizedEventKind`
- `IPaymentEventDedupStore`
- `InMemoryPaymentEventDedupStore`
- `PaymentWebhookIngestor`

The envelope says: “Stripe says this verified payment event happened.” It intentionally does not say what a product, order, user, tenant, entitlement, or merchant should do.

## Initial Stripe mappings

| Stripe event type | Normalized kind | Important fields |
| --- | --- | --- |
| `checkout.session.completed` | `CheckoutSessionCompleted` | checkout session id, payment intent id when present, amount total/currency, session payment status, metadata |
| `checkout.session.expired` | `CheckoutSessionExpired` | checkout session id, payment intent id when present, canceled payment status |
| `payment_intent.succeeded` | `PaymentSucceeded` | payment intent id, amount/currency, succeeded status, metadata |
| `payment_intent.payment_failed` | `PaymentFailed` | payment intent id, amount/currency, failed status, metadata |
| `payment_intent.canceled` | `PaymentCanceled` | payment intent id, amount/currency, canceled status, metadata |
| `payment_intent.amount_capturable_updated` | `PaymentAuthorized` | payment intent id, capturable amount if present, authorized status |
| `refund.created` | `RefundCreated` | refund id, payment intent id when present, amount/currency, refund status |
| `refund.updated` | `RefundSucceeded`, `RefundFailed`, or `RefundCreated` | conservative status-based refund mapping |
| `charge.refunded` | `PaymentRefunded` | payment intent id when present, refunded amount/currency |

Unsupported verified events return an envelope with `Kind = Unknown` and `IsSupportedEvent = false`. They should be acknowledged safely by endpoint code and ignored or routed by the application layer as appropriate.

Refund mapping is conservative. M2 does not claim partial-vs-full refunds unless the event object safely carries enough information for the mapped shape.

## Idempotency

Stripe can deliver duplicate webhook events. M2 provides an optional in-memory dedup store for deterministic tests and simple hosts:

- the dedup key is `providerId + providerEventId`;
- the first accepted event returns `Accepted`;
- duplicate deliveries return `Duplicate`;
- `PaymentWebhookIngestor` does not publish or invoke business reactions.

Production applications should replace `InMemoryPaymentEventDedupStore` with durable app-owned persistence if duplicate resistance must survive process restarts.

## EventBus investigation and decision

Current `Dominatus.Core` event infrastructure is `AiEventBus`, exposed as a per-agent bus on `AiAgent.Events` and routed by `AiWorld.Mail` to agent event buses. Its comments describe append-only per-type buckets, typed waiters, single-threaded publish/consume assumptions, and at-most-one-active-waiter-per-event-type assumptions. Replay support can inject external replay events into an agent, but the event bus itself is not a durable cross-domain payment event ledger.

Answers for M2:

1. Dominatus has an event bus, but it is per-agent workflow/simulation infrastructure, not a general cross-application payment ingestion bus.
2. Events publish immediately into the target agent bus; there is no global staged payment boundary.
3. Existing persistence can replay selected runtime/replay events, but payment webhook ingestion is not automatically durable/auditable through Core persistence.
4. `AiEventBus` is best understood as internal agent/runtime coordination. External provider events may be bridged into it by an application, but the Stripe adapter should not choose an agent/world target.
5. A payment webhook can be deterministic once raw inputs are supplied, but injecting it directly into an `AiWorld` from the provider package would couple external HTTP timing to simulation behavior and require app-owned routing decisions.
6. M2 returns normalized ingestion results and dedup primitives. Application layers decide whether and when to bridge accepted events into mailboxes, blackboards, `AiEventBus`, durable storage, or business workflows.
7. Boundary: Stripe package verifies/maps; base payment package provides neutral event/dedup primitives; application owns endpoint hosting and explicit injection into any world/event bus.

Therefore M2 does **not** add automatic EventBus publication or a `VerifyAndPublish` API. This avoids bending Core event semantics around payments and keeps Leviathan/application decisions out of Dominatus.Pay.

## Minimal ASP.NET-style sketch

This is intentionally a sketch, not a hosted endpoint in Dominatus.Pay:

```csharp
app.MapPost("/stripe/webhook", async (HttpRequest request, StripeWebhookVerifier verifier, IPaymentEventDedupStore dedup) =>
{
    using var reader = new StreamReader(request.Body);
    var rawBody = await reader.ReadToEndAsync();
    var sig = request.Headers["Stripe-Signature"].ToString();

    var result = await verifier.VerifyAndNormalizeAsync(rawBody, sig, new StripeWebhookOptions
    {
        EndpointSecret = configuration["Stripe:WebhookSecret"]!
    });

    if (!result.Verified) return Results.BadRequest(result.SafeError);
    if (result.NormalizedEvent is null || !result.IsSupportedEvent) return Results.Ok();

    var ingestion = await new PaymentWebhookIngestor().IngestAsync(result.NormalizedEvent, dedup);
    if (ingestion.Duplicate) return Results.Ok();

    // Application/Leviathan layer decides: enqueue fulfillment, store sale, publish to AiWorld, etc.
    return Results.Ok();
});
```

## Safety checklist

- Never log endpoint secrets, signature headers, API keys, client secrets, or raw payment credentials.
- Do not parse, mutate, or reserialize the request body before verification.
- Public live webhook endpoints should use HTTPS.
- Duplicate events are expected; use dedup before app reactions.
- Unknown verified events should not crash business flow.
- Dominatus.Pay must not grant access, fulfill orders, notify users, record sales, decide tenant/merchant routing, or apply platform policy.
