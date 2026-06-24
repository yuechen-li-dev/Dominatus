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

## Not in M1

- Webhooks.
- Square or PayPal.
- Subscriptions, invoices, tax, disputes, payouts, and KYC workflows.
