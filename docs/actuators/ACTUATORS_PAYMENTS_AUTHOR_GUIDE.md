# Dominatus.Pay author guide

This guide is for future Dominatus maintainers, Codex runs, ChatGPT instances, and provider-adapter authors. It is not an end-user integration tutorial and it is not a legal, tax, PCI, or processor-compliance guide.

Use this document when changing `Dominatus.Actuators.Payments`, adding provider adapter packages, reviewing payment-related pull requests, or handing payment work to a separate application/platform team.

Dominatus is the kernel. Dominatus.Pay gives workflows safe payment primitives. Payment providers are backend infrastructure, not application architecture. Leviathan owns product/commercial payment policy.

Prominent doctrine:

- No hidden fees.
- No phone-home.
- No package-distribution-based fee behavior.
- Provider adapters translate explicit command fields; they do not invent business policy.

## Purpose

Dominatus.Pay documentation exists to keep payment architecture from drifting across milestones and teams. Future authors should be able to read this file without conversation context and understand where payment primitives belong, where provider-specific code belongs, and where application policy belongs.

This guide intentionally focuses on authoring and maintenance decisions:

- what the Dominatus kernel owns;
- what provider adapter packages own;
- what a future Leviathan/application layer owns;
- which invariants must not be weakened;
- how future adapters should be reviewed before they ship.

It does not teach merchants how to configure a production Stripe, PayPal, Square/Block, tax, invoice, subscription, payout, or dispute program. Production deployments must verify current provider documentation and legal/compliance requirements independently.

## Layer separation

| Layer | Owns | Must not own |
| --- | --- | --- |
| `Dominatus.Core` | Runtime primitives, workflow execution, blackboards, typed actuation path, policy gates around side effects, audit-oriented execution structure. | Provider SDKs, provider account configuration, payment business policy, merchant onboarding, payment UX, billing dashboards, payout or reconciliation products. |
| `Dominatus.Actuators.Payments` | Provider-neutral payment commands, command validation, provider-neutral result/status shapes, idempotency requirements, provider registry, fake provider, provider adapter contract, actuation handler path, audit-friendly metadata, safe error sanitization expectations, risk/policy documentation. | Tenant identity, merchant registry, application registry, connected-account onboarding, provider account ownership, platform fee policy, commercial routing policy, end-user payment UX, dashboards, billing reports, payout/reconciliation UX, legal/compliance decisions, tax/invoice/subscription/dispute policy, provider SDK dependencies. |
| Provider adapter packages | Translation between Dominatus commands and one provider API, provider options, provider SDK dependency, provider idempotency mapping, provider-specific money conversion, explicit platform-fee mapping when supported and configured, provider status/error normalization, webhook verification in future milestones, optional gated live smoke tests. | Kernel payment semantics, application identity models, fee policy, default fees, hidden runtime conditions, cross-provider routing policy, end-user UX, dashboards, business reactions to payment events. |
| Leviathan/application layer | Merchant/user/application registry, provider account configuration storage, connected-account onboarding, routing policy, platform fee policy, fee disclosure UX, approval gates by workflow, dashboards, billing/reconciliation UX, business reaction to payment status/events, legal/compliance decisions. | Provider SDK details inside workflow code, changing Dominatus.Pay to know Leviathan identity models, hidden fee insertion inside provider adapters, kernel-level ownership of product/commercial policy. |

## Package map

| Package | Current or future role | Dependency rule |
| --- | --- | --- |
| `Dominatus.Actuators.Payments` | Current provider-neutral base package. Defines typed commands/results/statuses, validation, registry, fake provider, idempotency semantics, platform-fee metadata model, and actuation registration. | Must stay provider-SDK-free and network-free except through caller-supplied provider implementations. |
| `Dominatus.Actuators.Payments.Stripe` | Current Stripe.net adapter. Supports hosted Checkout Sessions, PaymentIntent create/capture/refund/cancel/status, idempotency mapping, explicit optional Connect platform-fee mapping, destination-charge validation, Stripe money conversion, error sanitization, offline tests, and gated live smoke tests. | May depend on Stripe.net. Must not leak Stripe concepts into the base package except through provider-neutral results/errors. |
| Future `Dominatus.Actuators.Payments.PayPal` | Future PayPal Orders adapter candidate. Expected to translate Dominatus commands to PayPal APIs where semantics match and clearly fail unsupported features. | PayPal SDK/API dependencies belong only in this adapter package. |
| Future `Dominatus.Actuators.Payments.Square` | Future Square/Block adapter candidate. Expected to translate Dominatus commands to Square APIs where semantics match and clearly fail unsupported features. | Square/Block SDK/API dependencies belong only in this adapter package. |

## Core invariants

Future authors must preserve these invariants:

- The base payments package stays provider-SDK-free.
- Provider SDK dependencies only live in provider adapter packages.
- External money-moving commands require idempotency.
- The fake provider remains deterministic, in-memory, and network-free.
- Dominatus.Pay must not handle raw card data.
- Dominatus.Pay must not add hidden fees, platform-default fees, distribution-based fees, or phone-home behavior.
- Platform fee metadata is explicit caller input only.
- Provider adapters translate explicit command fields; they do not invent business policy.
- Provider errors must be sanitized before they reach generic workflow results, logs, or user-facing surfaces.
- Live provider tests are skipped by default and must require explicit opt-in configuration.
- Status/result mapping must be provider-neutral.
- Unknown or provider-specific statuses must not force generic workflows to understand one provider's native state machine.
- Documentation must mention provider-specific limitations honestly.
- Secrets, API keys, signing keys, Authorization headers, and client secrets must never be logged.
- Fake-provider behavior should test Dominatus semantics, not become a mock clone of one provider.

## Provider adapter checklist

Use this checklist when adding or reviewing an adapter.

### Package naming

- Name the package `Dominatus.Actuators.Payments.<Provider>`.
- Keep namespaces under the package namespace.
- Keep registration helpers explicit, such as `AddStripe`, `AddPayPal`, or `AddSquare`.
- Do not place provider code in `Dominatus.Actuators.Payments`.

### Dependencies

- Add the provider SDK/API dependency only to the adapter package.
- Keep the base package free of provider SDK references, provider request/response types, and provider constants.
- Avoid optional reflection-based provider loading that hides dependency boundaries.

### Provider options

- Model API keys and endpoint/account options in provider-specific options types.
- Treat secrets as secrets: never log them, never include them in exceptions, and never echo them in test output.
- Add explicit options for platform-fee support, marketplace/destination routing, API versions, or environment selection when the provider requires them.
- Default risky behavior off.

### Command mappings

- Map each Dominatus command deliberately to provider APIs.
- Preserve Dominatus command validation and fail fast for unsupported combinations.
- Do not infer tenant, merchant, app, or fee policy from environment variables, package source, NuGet distribution, official builds, or hidden runtime checks.
- Return provider-neutral results and keep provider-specific details in bounded metadata/error fields.

### Idempotency mapping

- Map Dominatus idempotency keys to the provider's idempotency mechanism for external money-moving operations.
- If a provider lacks equivalent idempotency, document the limitation and fail or compensate conservatively.
- Test same-key/same-payload behavior and conflicting same-key behavior in fake or offline seams where feasible.

### Money conversion

- Convert Dominatus `PaymentMoney` to provider money units deterministically.
- Do not round silently.
- Reject negative amounts, ambiguous currencies, and fractional minor units that cannot be represented safely.
- Document provider currency limitations.

### Platform fee mapping, if supported

- Map platform fees only from explicit command metadata.
- Require provider options to opt in before mapping fees.
- Validate provider-specific routing requirements before making the provider request.
- Fail safely and clearly when a supplied fee cannot be mapped.
- Never add implicit fees.

### Unsupported features

- Return clear unsupported-feature failures rather than silently dropping requested behavior.
- Document unsupported provider features in the adapter doc.
- Do not expand the base API only to expose one provider quirk unless a provider-neutral concept exists.

### Error sanitization

- Sanitize provider errors by default.
- Preserve safe diagnostic fields such as provider error type, code, request id, or correlation id when available.
- Do not include API keys, client secrets, raw request bodies containing secrets, signing keys, or raw payment credentials.

### Offline fake/seam tests

- Add offline tests that do not require provider credentials or network access.
- Test command mapping, validation, idempotency, money conversion, unsupported features, error sanitization, and platform-fee gating.
- Keep tests deterministic.

### Optional live smoke tests

- Live smoke tests must be skipped by default.
- Require explicit environment variables and test-mode credentials where the provider supports test mode.
- Reject production/live credentials unless a future milestone explicitly designs a protected production smoke workflow.
- Keep amounts low and avoid completing real customer payment flows.

### Docs updates

- Add or update adapter docs under `docs/actuators/`.
- Link back to this author guide.
- Document supported commands, unsupported features, idempotency behavior, platform-fee behavior, live test gating, and production cautions.

### NuGet publish workflow updates

- Add the adapter package to NuGet/release workflows only after offline tests and docs exist.
- Ensure package metadata identifies the adapter as provider-specific.
- Do not add publish behavior that changes fees, telemetry, or runtime behavior based on package distribution.

## Platform fee rules

Dominatus models explicit fee metadata so provider adapters can safely translate caller intent into provider APIs. Dominatus.Pay does not decide whether a platform fee exists.

Leviathan or another application/control-plane layer decides:

- whether a platform fee exists;
- how much it is;
- who receives it;
- how it is disclosed;
- which merchant/provider account is used;
- whether a payment flow is allowed;
- what UX surrounds the payment.

A provider adapter may map a fee only when all of these are true:

- the command contains fee metadata;
- provider options allow platform-fee mapping;
- provider-specific routing requirements are satisfied.

Required adapter behavior:

- Fail safely if fee metadata is supplied but cannot be safely mapped.
- Never silently drop an explicit fee unless the docs/API explicitly mark the provider or command as unsupported and the adapter returns a clear failure.
- Never add an implicit fee.
- Never apply default fees.
- Never infer fee behavior from package distribution, NuGet source, official builds, tenant identity, environment, or hidden runtime condition.
- Never phone home to decide fee behavior.

## Idempotency rules

External money-moving commands require idempotency because retries are normal in distributed systems and payment side effects are expensive to duplicate.

Dominatus command semantics stay stable even when future providers differ:

- Creation, capture, refund, and cancel operations must carry a stable idempotency key.
- Read/status operations should not invent idempotency requirements when the provider treats them as safe reads.
- Provider adapters map the Dominatus key to provider-specific idempotency fields or headers.
- Stripe uses idempotency keys for POST operations only; `GetPaymentStatusCommand` maps to a retrieve/GET-style operation and sends no idempotency key.
- Repeating the same command with the same idempotency key and same payload should be safe.
- Reusing the same idempotency key with conflicting payloads should fail or surface a provider conflict; test this in fake/offline seams where feasible.
- If a future provider cannot guarantee equivalent behavior, document it and prefer conservative failure over duplicate money movement.

## Status mapping doctrine

Provider statuses must be normalized to Dominatus statuses conservatively.

Rules:

- Unknown provider status maps to `Unknown`.
- Do not overclaim success.
- Pending, processing, waiting-for-customer, and challenge states should stay `Pending`, `RequiresAction`, or another conservative Dominatus state rather than `Succeeded`.
- Authorization-only states should not be reported as captured/settled success.
- Refunds and partial refunds must preserve provider nuance when possible.
- Provider-specific states may be retained in safe metadata for diagnostics, but generic workflows should branch on Dominatus statuses.
- Document lossy mappings and provider limitations.

## Webhook/event future seam

This section is design guidance for future M2 webhook/status synchronization work. It is not a current API guarantee unless corresponding APIs already exist in the codebase.

Desired future shape:

- Provider webhooks should become normalized Dominatus payment events.
- Webhook verification belongs in the provider adapter package because signatures, headers, and event envelopes are provider-specific.
- Event ingestion should be idempotent.
- Duplicate webhook delivery must be safe.
- Raw webhook payload should be stored only when safe, configured, and necessary for audit/debugging.
- Secrets and signing keys must never be logged.
- Events should update payment status through explicit commands/results or event handlers rather than hidden provider side effects.
- The application layer decides business reactions to events.

Potential future normalized event names:

- `PaymentCreated`
- `PaymentRequiresAction`
- `PaymentAuthorized`
- `PaymentCaptured`
- `PaymentSucceeded`
- `PaymentCanceled`
- `PaymentFailed`
- `PaymentRefunded`
- `PaymentPartiallyRefunded`
- `CheckoutSessionCreated`
- `CheckoutSessionCompleted`
- `CheckoutSessionExpired`

When this seam is implemented, keep the split clear: adapters verify and normalize provider events; Dominatus defines provider-neutral event/result shapes; Leviathan/application code decides what the product does next.

## Leviathan handoff

Leviathan and future application/platform teams should consume Dominatus.Pay as a reusable payment actuator layer, not as an application platform hidden inside the kernel.

Leviathan should own:

- merchant, user, tenant, and application registry;
- provider account configuration storage;
- connected-account onboarding;
- routing policy across merchants/providers/accounts;
- construction of Dominatus payment commands from approved application workflows;
- explicit platform fee metadata when policy requires it;
- user-facing fee disclosure;
- dashboards, reports, reconciliation UX, and operational review tools;
- approval gates by workflow;
- business reactions to provider-normalized payment status/events when those exist;
- legal, compliance, tax, invoice, subscription, dispute, payout, and regional policy decisions.

Leviathan should not require Dominatus.Pay to know Leviathan identity models. Dominatus.Pay should remain reusable without Leviathan, including in self-hosted MIT deployments, tests, and non-Leviathan applications.

The handoff contract is straightforward: Leviathan decides policy and constructs explicit commands; Dominatus.Pay validates and routes typed payment primitives; provider adapters translate those explicit fields to provider infrastructure.

## Bad ideas / anti-patterns

Do not:

- add Stripe, Square/Block, PayPal, or any other provider SDK to the base package;
- put Leviathan concepts into Dominatus.Pay;
- add hidden fees;
- add phone-home behavior;
- add package-distribution-based fee behavior;
- let a provider adapter choose platform fee policy;
- store raw card data;
- make live tests run by default;
- leak API keys, client secrets, signing keys, or Authorization headers;
- make provider-specific statuses leak into generic workflows unless wrapped in provider-neutral metadata;
- turn the fake provider into a mock of one provider instead of a provider-neutral test backend;
- hide unsupported features by pretending they succeeded;
- couple payment routing to NuGet package source, official builds, or runtime identity guesses.

## Future roadmap

Dominatus-owned roadmap candidates:

- M2: webhook verification and normalized event ingestion.
- M3: PayPal Orders adapter.
- M4: Square/Block adapter.
- M5: reconciliation/reporting primitives only if they remain kernel-level, provider-neutral, and not product dashboard policy.

Leviathan/application-owned roadmap candidates:

- Merchant registry.
- Provider account and connected-account onboarding UX.
- Routing policy.
- Platform fee policy and disclosure UX.
- Dashboards, billing reports, payout/reconciliation UX, and operational review.
- Business responses to normalized payment events.

Keep revisiting this split before adding payment features. If a change makes Dominatus.Pay know who the merchant is, what fee should be charged, or what product UX should happen, it probably belongs in Leviathan or another application/control-plane layer instead.

## M2 webhook/event seam decision

M2 implemented Stripe webhook verification and normalized event ingestion without turning Dominatus.Pay into an application payment platform.

Implemented seam:

- `Dominatus.Actuators.Payments` owns `PaymentEventEnvelope`, `PaymentNormalizedEventKind`, `IPaymentEventDedupStore`, `InMemoryPaymentEventDedupStore`, and `PaymentWebhookIngestor`.
- `Dominatus.Actuators.Payments.Stripe` owns `StripeWebhookOptions`, `StripeWebhookProcessingResult`, `StripeWebhookVerifier`, and conservative Stripe-to-Dominatus event mapping.
- The Stripe verifier validates the exact raw request body plus `Stripe-Signature` and endpoint secret using Stripe.net before parsing/mapping.
- Unknown verified provider events return `Kind = Unknown` / unsupported rather than being treated as signature failures.
- Dedup is optional and provider-neutral; durable production persistence remains app-owned.

EventBus boundary:

- Core `AiEventBus` is per-agent runtime coordination, optimized for typed waiters in a Dominatus world/agent loop.
- M2 does not automatically publish payment webhooks into `AiEventBus` because the provider adapter does not know which world, agent, mailbox, tenant, order, or business workflow owns the event.
- Applications may explicitly bridge an accepted `PaymentEventEnvelope` into an `AiWorld`, mailbox, durable queue, or business service after verification and dedup.
- Future provider adapters should expose verified normalized events and optional dedup, not app reactions.

Future provider webhook checklist:

- Verify provider signatures before parsing payloads.
- Require exact raw request input and provider signature/header input.
- Sanitize every failure path and never echo secrets/signatures/API keys/client secrets.
- Map only provider events with a clear provider-neutral meaning.
- Return unknown/unsupported verified events safely.
- Keep duplicate-delivery protection provider-neutral and make durable stores app-owned.
- Do not add endpoint hosting, tenant/merchant registry, dashboards, fulfillment, entitlement grants, or commercial policy to Dominatus.Pay.


## PayPal adapter checklist (M3)

* Use PayPal Orders v2 for hosted approval flows; do not collect raw card data in Dominatus.Pay.
* Map Dominatus idempotency keys to `PayPal-Request-Id` only on supported POST calls. Keep derived request ids deterministic and within PayPal's 38 single-byte character limit.
* Be explicit about provider references: checkout/payment creation returns an order id, order capture can return a capture id, refunds require a capture id, and M3 status lookup expects an order id.
* Do not silently drop `PaymentPlatformFee`. PayPal partner/platform-fee behavior remains deferred until partner docs and account-routing policy are verified outside the kernel adapter.
* Keep PayPal webhooks as a future seam. A later milestone must verify webhook authenticity, tolerate retries, and follow the normalized event/dedup pattern established for Stripe M2.
