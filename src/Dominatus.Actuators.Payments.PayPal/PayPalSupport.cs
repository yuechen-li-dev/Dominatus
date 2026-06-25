using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dominatus.Actuators.Payments;

namespace Dominatus.Actuators.Payments.PayPal;

internal static class PayPalMoney
{
    private static readonly HashSet<string> Zero = ["BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"];
    public static PayPalAmount ToPayPal(PaymentMoney money)
    {
        var scale = Zero.Contains(money.Currency) ? 0 : 2;
        var units = money.Amount * (decimal)Math.Pow(10, scale);
        if (decimal.Truncate(units) != units) throw new ArgumentException($"Amount {money.Amount} {money.Currency} is not representable with PayPal currency precision.");
        return new PayPalAmount(money.Currency, money.Amount.ToString(scale == 0 ? "0" : "0.00", CultureInfo.InvariantCulture));
    }
    public static PaymentMoney FromPayPal(PayPalAmount? amount) => amount is null ? new PaymentMoney(0, "USD") : new PaymentMoney(decimal.Parse(amount.Value, CultureInfo.InvariantCulture), amount.CurrencyCode);
}

internal static class PayPalRequestId
{
    public static string? From(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        return "dom-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32].ToLowerInvariant();
    }
}

public static class PayPalStatusMapper
{
    public static CheckoutSessionStatus MapCheckout(string? status) => Norm(status) switch { "COMPLETED" => CheckoutSessionStatus.Completed, "VOIDED" => CheckoutSessionStatus.Canceled, "CREATED" or "SAVED" or "APPROVED" or "PAYER_ACTION_REQUIRED" => CheckoutSessionStatus.Open, _ => CheckoutSessionStatus.Unknown };
    public static PaymentStatus MapOrder(string? status) => Norm(status) switch { "CREATED" => PaymentStatus.Created, "SAVED" => PaymentStatus.Pending, "APPROVED" => PaymentStatus.RequiresAction, "VOIDED" => PaymentStatus.Canceled, "COMPLETED" => PaymentStatus.Succeeded, "PAYER_ACTION_REQUIRED" => PaymentStatus.RequiresAction, _ => PaymentStatus.Unknown };
    public static PaymentStatus MapCapture(string? status) => Norm(status) switch { "COMPLETED" => PaymentStatus.Captured, "PENDING" => PaymentStatus.Pending, "DECLINED" or "FAILED" => PaymentStatus.Failed, "REFUNDED" => PaymentStatus.Refunded, "PARTIALLY_REFUNDED" => PaymentStatus.PartiallyRefunded, _ => PaymentStatus.Unknown };
    public static RefundStatus MapRefund(string? status) => Norm(status) switch { "COMPLETED" => RefundStatus.Succeeded, "PENDING" => RefundStatus.Pending, "FAILED" or "CANCELLED" => RefundStatus.Failed, _ => RefundStatus.Unknown };
    private static string Norm(string? status) => (status ?? string.Empty).Trim().ToUpperInvariant();
}

internal static class PayPalSanitizer
{
    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
        var redacted = Regex.Replace(input, "Bearer\\s+[A-Za-z0-9._~+/=-]+", "Bearer [redacted]", RegexOptions.IgnoreCase);
        redacted = Regex.Replace(redacted, "(client_secret\\\"?\\s*[:=]\\s*\\\"?)[^\\\"&\\s,}]+", "$1[redacted]", RegexOptions.IgnoreCase);
        redacted = Regex.Replace(redacted, "(access_token\\\"?\\s*[:=]\\s*\\\"?)[^\\\"&\\s,}]+", "$1[redacted]", RegexOptions.IgnoreCase);
        redacted = Regex.Replace(redacted, "[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}", "[redacted-email]");
        return redacted.Length > 400 ? redacted[..400] : redacted;
    }
    public static Exception ToPaymentException(string providerId, Exception ex, PayPalPaymentProviderOptions options)
    {
        var message = $"PayPal provider '{providerId}' request failed.";
        if (options.IncludeRawProviderErrors)
            message += " " + Redact(ex.Message).Replace(options.ClientId, "[redacted]").Replace(options.ClientSecret, "[redacted]");
        return new InvalidOperationException(message, ex);
    }
}
