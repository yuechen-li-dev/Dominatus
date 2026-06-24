using System.Text.RegularExpressions;
using Stripe;

namespace Dominatus.Actuators.Payments.Stripe;

public static class StripePaymentErrorMapper
{
    public static InvalidOperationException ToPaymentException(string providerId, Exception exception, StripePaymentProviderOptions options)
    {
        var stripe = exception as StripeException;
        var details = new List<string> { $"Stripe payment provider '{providerId}' request failed." };
        var requestId = stripe?.StripeResponse?.RequestId;
        if (!string.IsNullOrWhiteSpace(requestId)) details.Add($"Stripe request id: {requestId}.");
        if (!string.IsNullOrWhiteSpace(stripe?.StripeError?.Type)) details.Add($"Stripe error type: {stripe.StripeError.Type}.");
        if (!string.IsNullOrWhiteSpace(stripe?.StripeError?.Code)) details.Add($"Stripe error code: {stripe.StripeError.Code}.");
        details.Add(options.IncludeRawProviderErrors ? Sanitize(exception.Message) : "Provider error details were sanitized.");
        return new InvalidOperationException(string.Join(" ", details), exception);
    }
    public static string Sanitize(string value)
    {
        var sanitized = Regex.Replace(value, "sk_(live|test)_[A-Za-z0-9_]+", "[redacted-stripe-api-key]", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"(client_secret|clientSecret)\s*[:=]\s*[^\s,;]+", "$1=[redacted-client-secret]", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, "pi_[A-Za-z0-9]+_secret_[A-Za-z0-9]+", "[redacted-client-secret]", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"Authorization\s*[:=]\s*Bearer\s+[^\s,;]+", "Authorization: Bearer [redacted]", RegexOptions.IgnoreCase);
        return sanitized;
    }
}
