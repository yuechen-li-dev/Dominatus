# Dominatus.Pay M0: provider-neutral payment actuators

Dominatus.Pay is the first payment actuator contract for Dominatus. It gives agents typed commands for payment work while keeping payment processors interchangeable behind backend adapters.

## Purpose

Agent workflows should not call Stripe, Square/Block, PayPal, or other provider SDKs directly. They should issue typed Dominatus actuation commands:

- `CreateCheckoutSessionCommand`
- `CreatePaymentIntentCommand`
- `CapturePaymentCommand`
- `RefundPaymentCommand`
- `CancelPaymentCommand`
- `GetPaymentStatusCommand`

Dominatus owns workflow state, policy gates, idempotency expectations, audit metadata, explicit platform fee disclosure, and provider selection. Provider adapters own provider API translation, provider-specific response mapping, later webhook verification, and error mapping.

## Non-goals

M0 is not a full payment platform. It does not include live provider SDKs, network calls, API keys, telemetry, hidden fees, PCI card-data handling, stored payment credentials, webhooks, marketplace onboarding, subscriptions, invoices, tax, payouts, disputes, chargebacks, KYC/KYB, wallets, or crypto.

## Provider-neutral contract

The package lives at `src/Dominatus.Actuators.Payments` with namespace `Dominatus.Actuators.Payments`. The user-facing façade name is **Dominatus.Pay**.

Money uses `PaymentMoney(decimal Amount, string Currency)` to avoid floating point arithmetic. M0 validates non-negative amounts and a simple ISO-like three-letter currency code, but intentionally avoids complex minor-unit currency math.

`IPaymentProvider` exposes one method for each M0 command. `PaymentProviderRegistry` registers providers by id and rejects duplicate ids case-insensitively.

## Explicit platform fee model

`PaymentPlatformFee` is always explicit. A command can opt in with a fixed amount, percentage, description, and optional platform account id. There is no default fee, hidden fee, telemetry, or phone-home behavior.

When a fee is supplied, validation requires at least a fixed amount or percent, requires percent to be between 0 and 100, and requires fixed-fee currency to match the payment currency. Results preserve a human-readable `PlatformFeeDisclosure` suitable for audit logs and UI review.

Open-source MIT users can compile, run, and change the code themselves. Managed Dominatus workflows may apply explicit platform fees through configured platform accounts, but those fees must remain visible and should be reviewed for processor, legal, and compliance requirements.

## Fake provider

`FakePaymentProvider` is deterministic, in-memory, and network-free. It creates ids such as `fake_chk_00000001`, `fake_pi_00000001`, and `fake_ref_00000001`. Checkout URLs use `https://payments.local/checkout/{id}`.

The fake provider supports checkout session creation, automatic payment intents, manual authorization/capture, cancel, status lookup, full refunds, partial refunds, platform fee disclosure preservation, and idempotency.

## Idempotency

Money-moving commands require an idempotency key. The fake provider stores a fingerprint of the command payload for each key:

- same key and same payload returns the original result;
- same key and conflicting payload fails with an idempotency conflict.

Production workflows should pass stable keys derived from their own durable workflow/order ids.

## Approval and policy guidance

Payments are external financial effects. Recommended policy treatment:

- `CreateCheckoutSessionCommand`: external financial effect; may require approval depending on workflow.
- `CreatePaymentIntentCommand`: external financial effect; usually requires approval or explicit user initiation.
- `CapturePaymentCommand`: external financial effect; require approval unless directly user-confirmed.
- `RefundPaymentCommand`: external financial and potentially destructive; require approval.
- `CancelPaymentCommand`: external effect; may require approval.
- `GetPaymentStatusCommand`: read-only lookup; normally no approval.

Use Dominatus actuation policies around these typed commands rather than embedding approval logic in provider adapters.

## Fake provider usage example

```csharp
using Dominatus.Actuators.Payments;
using Dominatus.Core.Runtime;

var registry = new PaymentProviderRegistry()
    .Register(new FakePaymentProvider());

var host = new ActuatorHost()
    .RegisterPaymentActuators(registry);

var command = new CreateCheckoutSessionCommand(
    ProviderId: "fake",
    IdempotencyKey: "order-123:create-checkout",
    SuccessUrl: "https://example.test/success",
    CancelUrl: "https://example.test/cancel",
    Items:
    [
        new PaymentLineItem
        {
            Name = "Widget",
            UnitAmount = new PaymentMoney(10m, "USD"),
            Quantity = 1
        }
    ],
    PlatformFee: new PaymentPlatformFee
    {
        Percent = 5m,
        Description = "Managed Dominatus workflow platform fee",
        PlatformAccountId = "configured-platform-account"
    });
```

## Future live provider adapters

Future packages can add Stripe, Square/Block, PayPal, or other adapters by implementing `IPaymentProvider`. Each adapter should be small and boring: translate typed commands to provider APIs, map results/errors back to Dominatus models, verify webhooks later, and keep secrets out of actuation errors and logs.

Before implementing live adapters, verify current official provider docs, supported API versions, compliance obligations, idempotency semantics, platform-fee/marketplace rules, and webhook verification requirements.

## Production warnings

Production deployments must verify processor documentation and legal/compliance requirements. Do not handle raw card numbers in Dominatus. Prefer hosted checkout or provider-hosted/payment-element components to reduce PCI scope. Review platform fees, marketplaces, connected accounts, taxes, refunds, disputes, payouts, and regional requirements with qualified counsel and processor guidance.
