using System.Text.Json.Serialization;

namespace Dominatus.Actuators.Payments.PayPal;

public enum PayPalEnvironment
{
    Sandbox,
    Live
}

public sealed record PayPalPaymentProviderOptions
{
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public string ProviderId { get; init; } = "paypal";
    public PayPalEnvironment Environment { get; init; } = PayPalEnvironment.Sandbox;
    public bool IncludeRawProviderErrors { get; init; } = false;
    internal Uri? BaseUrl { get; init; }

    public override string ToString() =>
        $"PayPalPaymentProviderOptions {{ ClientId = {PayPalSanitizer.Redact(ClientId)}, ProviderId = {ProviderId}, Environment = {Environment}, IncludeRawProviderErrors = {IncludeRawProviderErrors} }}";
}

internal sealed record PayPalAmount(
    [property: JsonPropertyName("currency_code")] string CurrencyCode,
    [property: JsonPropertyName("value")] string Value);

internal sealed record PayPalCreateOrderRequest(
    [property: JsonPropertyName("intent")] string Intent,
    [property: JsonPropertyName("purchase_units")] IReadOnlyList<PayPalPurchaseUnit> PurchaseUnits,
    [property: JsonPropertyName("application_context")] PayPalApplicationContext? ApplicationContext = null,
    [property: JsonPropertyName("payer")] PayPalPayer? Payer = null);

internal sealed record PayPalPurchaseUnit(
    [property: JsonPropertyName("amount")] PayPalAmount Amount,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("custom_id")] string? CustomId = null);

internal sealed record PayPalApplicationContext(
    [property: JsonPropertyName("return_url")] string ReturnUrl,
    [property: JsonPropertyName("cancel_url")] string CancelUrl);

internal sealed record PayPalPayer(
    [property: JsonPropertyName("email_address")] string EmailAddress);

internal sealed record PayPalCaptureAuthorizationRequest(
    [property: JsonPropertyName("amount")] PayPalAmount? Amount = null,
    [property: JsonPropertyName("final_capture")] bool FinalCapture = true);

internal sealed record PayPalRefundRequest(
    [property: JsonPropertyName("amount")] PayPalAmount? Amount = null,
    [property: JsonPropertyName("note_to_payer")] string? NoteToPayer = null);

internal sealed record PayPalLinkDto(
    [property: JsonPropertyName("href")] string Href,
    [property: JsonPropertyName("rel")] string Rel,
    [property: JsonPropertyName("method")] string? Method = null);

internal sealed record PayPalOrderDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("links")] IReadOnlyList<PayPalLinkDto>? Links = null,
    [property: JsonPropertyName("purchase_units")] IReadOnlyList<PayPalPurchaseUnitDto>? PurchaseUnits = null);

internal sealed record PayPalPurchaseUnitDto(
    [property: JsonPropertyName("payments")] PayPalPaymentsDto? Payments = null,
    [property: JsonPropertyName("amount")] PayPalAmount? Amount = null);

internal sealed record PayPalPaymentsDto(
    [property: JsonPropertyName("captures")] IReadOnlyList<PayPalCaptureDto>? Captures = null,
    [property: JsonPropertyName("authorizations")] IReadOnlyList<PayPalAuthorizationDto>? Authorizations = null);

internal sealed record PayPalCaptureDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("amount")] PayPalAmount? Amount = null);

internal sealed record PayPalAuthorizationDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("amount")] PayPalAmount? Amount = null);

internal sealed record PayPalRefundDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("amount")] PayPalAmount? Amount = null);
