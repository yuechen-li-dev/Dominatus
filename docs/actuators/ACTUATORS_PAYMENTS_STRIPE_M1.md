# Dominatus.Pay M1 — Stripe Checkout adapter

`Dominatus.Actuators.Payments.Stripe` is the Stripe backend adapter for the provider-neutral `IPaymentProvider` abstraction. M1 intentionally keeps the adapter boring: typed Dominatus payment commands are translated into Stripe-hosted Checkout Sessions, PaymentIntents, capture, refund, cancel, and status lookup calls.

## Supported M1 commands

- `CreateCheckoutSessionCommand` creates a Stripe-hosted Checkout Session in `payment` mode with inline `line_items.price_data`.
- `CreatePaymentIntentCommand` creates a Stripe PaymentIntent with automatic or manual capture.
- `CapturePaymentCommand` captures an authorized PaymentIntent.
- `RefundPaymentCommand` creates a refund for a PaymentIntent.
- `CancelPaymentCommand` cancels a PaymentIntent.
- `GetPaymentStatusCommand` retrieves PaymentIntent status without sending an idempotency key.

No raw card data is handled by Dominatus. Hosted Checkout should be preferred for browser-facing payment collection.

## Configuration

```csharp
var registry = new PaymentProviderRegistry().AddStripe(new StripePaymentProviderOptions
{
    ApiKey = "sk_test_...",
    ProviderId = "stripe",
    EnablePlatformFees = false,
    UseDestinationCharges = false,
    ConnectedAccountId = null
});
```

Options:

- `ApiKey` is required and secret. Never log it.
- `ProviderId` defaults to `stripe`.
- `EnablePlatformFees` must be `true` before any command with `PaymentPlatformFee` is accepted.
- `UseDestinationCharges` must be `true` for M1 platform-fee mapping.
- `ConnectedAccountId` is required when destination-charge platform fees are enabled.
- `IncludeRawProviderErrors` defaults to `false`; keep it off unless debugging in a safe environment.

## Idempotency

Dominatus command idempotency keys are mapped to Stripe `RequestOptions.IdempotencyKey` for POST operations: checkout creation, PaymentIntent creation, capture, refund, and cancel. Status lookup is a GET-style retrieve and does not send an idempotency key.

Stripe rejects reuse of an idempotency key with mismatched parameters, so callers should use stable request payloads per key.

## Dominatus.Pay doctrine

Dominatus.Pay is a kernel/payment-actuator layer. It provides typed payment primitives, validation, idempotency, provider routing, adapter mapping, and audit-friendly result metadata. It does not own merchant identity, tenant registry, connected-account ownership, platform fee policy, or commercial routing rules. Those belong to an application/control-plane layer such as Leviathan.

Dominatus.Pay may carry explicit platform-fee metadata because provider adapters need a safe way to translate caller intent. The application layer decides whether a platform fee exists, who receives it, and how it is disclosed to users. The Stripe adapter never applies platform fees by default.

Operational consequences:

- No hidden fees.
- No phone-home.
- No package-distribution-based fee behavior.
- Self-hosted MIT users can pass no platform fee, use their own provider accounts, or modify the code.
- Stripe adapter code only maps explicit command fields to Stripe API calls; it must not infer platform fees from package source, distribution channel, tenant identity, or runtime environment.

## Platform fees and Connect destination charges

M1 maps explicit `PaymentPlatformFee` only when all of the following are true:

1. `EnablePlatformFees = true`.
2. `UseDestinationCharges = true`.
3. `ConnectedAccountId` is configured.
4. The command includes `PaymentPlatformFee`.

When enabled, Dominatus computes `payment_intent_data.application_fee_amount` for Checkout or `application_fee_amount` for PaymentIntent. It also sets `transfer_data.destination` to `ConnectedAccountId`.

Fee behavior is explicit: fixed fee plus percent-of-gross, capped at the gross transaction amount, converted to Stripe minor units in the transaction currency. If only percent is provided, the adapter computes the amount from the command total. If only fixed amount is provided, that fixed amount is used.

> Warning: platform-fee and marketplace behavior must be verified for the deployer's Stripe Connect account, country, settlement currency, and legal/compliance requirements before production use.

## Money conversion

Stripe expects integer minor units for most currencies. M1 uses deterministic conversion, supports USD and other common two-decimal currencies plus common zero-decimal currencies such as JPY, rejects negative amounts, rejects fractional minor units, and rejects unsupported ambiguous currencies rather than rounding silently. Stripe request currencies are lowercase; Dominatus `PaymentMoney` remains uppercase/canonical.

## Safety

- Do not log `ApiKey`, Authorization headers, or client secrets.
- Use Stripe test mode first.
- Capture, refund, and cancel commands should be protected by application-level approval gates.
- Provider errors are sanitized by default; Stripe request id/type/code are preserved when available.

## Live smoke tests

The Stripe test project contains optional live Stripe **test-mode** smoke tests. They are skipped by default and are the only Stripe tests intended to call the Stripe network. Normal `dotnet test` and CI runs do not require Stripe credentials.

Required environment variables to enable live smoke tests:

- `DOMINATUS_STRIPE_LIVE_TESTS=1`
- `DOMINATUS_STRIPE_TEST_API_KEY=sk_test_...`

The test helper rejects keys that do not begin with `sk_test_`; live-mode keys such as `sk_live_...` are not accepted. The tests do not print the API key, do not print client secrets, do not require a real browser, do not require card entry, and do not complete Checkout Sessions.

Optional Connect/platform-fee smoke variables:

- `DOMINATUS_STRIPE_CONNECTED_ACCOUNT_ID=acct_...`
- `DOMINATUS_STRIPE_LIVE_PLATFORM_FEE_TESTS=1`

The Connect/platform-fee smoke test only runs when both optional variables are present in addition to the required live-test variables. When configured, it creates a low-value test-mode destination-charge Checkout Session with an explicit `PaymentPlatformFee` and asserts that the adapter returns platform-fee disclosure metadata. If Stripe rejects the configured connected account or Connect setup, the test should fail because the protected live-smoke environment is misconfigured.

Example local invocation on Linux/macOS:

```bash
export DOMINATUS_STRIPE_LIVE_TESTS=1
export DOMINATUS_STRIPE_TEST_API_KEY=sk_test_...
dotnet test tests/Dominatus.Actuators.Payments.Stripe.Tests/Dominatus.Actuators.Payments.Stripe.Tests.csproj -f net10.0 --filter FullyQualifiedName~Live
```

Example optional Connect smoke invocation:

```bash
export DOMINATUS_STRIPE_LIVE_TESTS=1
export DOMINATUS_STRIPE_TEST_API_KEY=sk_test_...
export DOMINATUS_STRIPE_CONNECTED_ACCOUNT_ID=acct_...
export DOMINATUS_STRIPE_LIVE_PLATFORM_FEE_TESTS=1
dotnet test tests/Dominatus.Actuators.Payments.Stripe.Tests/Dominatus.Actuators.Payments.Stripe.Tests.csproj -f net10.0 --filter FullyQualifiedName~PlatformFee
```

CI may enable these tests manually in a protected environment by setting the same variables, but default CI must not depend on Stripe network access. The smoke tests use unique idempotency keys per run and keep amounts low, such as USD 1.00. PaymentIntent tests create manual-capture PaymentIntents without card confirmation and attempt best-effort cancellation after assertions.

## Not in M1

- Webhooks.
- Square or PayPal.
- Subscriptions, invoices, tax, disputes, payouts, and KYC workflows.
