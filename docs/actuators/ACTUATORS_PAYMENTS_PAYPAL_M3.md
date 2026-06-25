# Dominatus.Pay M3 — PayPal Orders adapter

Dominatus.Actuators.Payments.PayPal adds a second real provider adapter while keeping `Dominatus.Actuators.Payments` provider-neutral. It uses an internal `HttpClient` PayPal REST client rather than a PayPal SDK because the current .NET SDK surface is fragmented compared with the Orders v2/Payments v2 REST APIs needed here.

## Verified PayPal docs

Checked on 2026-06-24:

* Orders v2 creates orders with `intent` `CAPTURE` or `AUTHORIZE`, `purchase_units`, and amount `currency_code`/`value`.
* Create-order responses use links such as `approve`, `self`, `capture`, and `authorize` depending on flow.
* Capturing an approved order uses the order capture endpoint; authorizing an approved order creates an authorization; authorizations can later be captured with Payments v2.
* Payments v2 refunds captures, not generic orders.
* REST idempotency uses `PayPal-Request-Id` on supported POST APIs. PayPal notes not every API supports it, recommends UUIDs, and documents a 38 single-byte character limit.
* GET/status lookup does not use an idempotency header.
* Webhooks are out of M3. Future webhook work must verify messages and handle PayPal retries.

## Supported M3 commands

* `CreateCheckoutSessionCommand` creates an Orders v2 `CAPTURE` order and returns the `approve` link as `CheckoutUrl`.
* `CreatePaymentIntentCommand` creates an Orders v2 order: automatic capture maps to `CAPTURE`; manual capture maps to `AUTHORIZE`.
* `CapturePaymentCommand` captures a PayPal order id. Authorization capture is left for a future typed-reference flow.
* `RefundPaymentCommand.PaymentId` is a PayPal capture id because PayPal refunds captures.
* `GetPaymentStatusCommand.PaymentId` is a PayPal order id in M3.
* `CancelPaymentCommand` fails clearly for generic order ids; unapproved PayPal orders are normally abandoned/expired. Void authorization support is deferred until the command can distinguish authorization references safely.

No raw card data is accepted or sent. The adapter only creates hosted PayPal approval flows and server-side order/payment calls.

## Idempotency

Dominatus idempotency keys are deterministically shortened to PayPal request ids with:

`dom-` + first 32 lowercase hex characters of SHA-256(original key)

The result is stable and 36 characters, within PayPal's limit. The adapter sends it as `PayPal-Request-Id` on supported POST operations (create order, capture order, authorize order, capture authorization, refund capture) and never on GET status calls.

## Money and statuses

Amounts are formatted as PayPal decimal strings with uppercase currency codes. Common zero-decimal currencies such as JPY use no decimals; other configured currencies use two decimals. Negative amounts and fractional minor units are rejected.

Order statuses map conservatively: `CREATED` to Created, `SAVED` to Pending, `APPROVED`/`PAYER_ACTION_REQUIRED` to RequiresAction, `VOIDED` to Canceled, `COMPLETED` to Succeeded, otherwise Unknown. Capture and refund statuses map to captured/succeeded, pending, failed, refunded, or Unknown as applicable.

## Platform fees and doctrine

The adapter does not invent platform-fee behavior and does not apply implicit fees. If a command includes `PlatformFee`, M3 fails with an unsupported-provider-feature style error instead of dropping it.

Dominatus.Pay owns neutral commands, validation, idempotency, safe errors, fake/offline testing, and normalized statuses. The PayPal adapter only translates explicit commands to PayPal APIs. Leviathan/application code owns merchant identity, account routing, UX, fulfillment, dashboards, and business policy.

## Errors, secrets, and testing

`ClientSecret`, OAuth access tokens, bearer tokens, `client_secret` fields, `access_token` fields, and payer emails are redacted from safe errors. `PayPalPaymentProviderOptions.ToString()` omits `ClientSecret`.

Unit tests use `IPayPalApiClient` fakes and make no live calls. Optional live sandbox tests are deferred; if added later they must be gated by `DOMINATUS_PAYPAL_LIVE_TESTS=1`, sandbox credentials, and no live production environment by default.
