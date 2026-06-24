using Dominatus.Actuators.Payments;

namespace Dominatus.Actuators.Payments.Stripe;

internal static class StripeMoney
{
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase) { "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF" };
    private static readonly HashSet<string> TwoDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase) { "USD", "EUR", "GBP", "CAD", "AUD", "NZD", "CHF", "SEK", "NOK", "DKK", "MXN", "BRL", "SGD", "HKD" };
    public static long ToMinorUnits(PaymentMoney money)
    {
        if (money.Amount < 0) throw new ArgumentOutOfRangeException(nameof(money), "Stripe amounts cannot be negative.");
        var exponent = Exponent(money.Currency);
        var scale = (decimal)Math.Pow(10, exponent);
        var minor = money.Amount * scale;
        if (decimal.Truncate(minor) != minor) throw new ArgumentException($"Amount {money.Amount} {money.Currency} is not representable in Stripe minor units.");
        if (minor > long.MaxValue) throw new ArgumentOutOfRangeException(nameof(money), "Stripe amount is too large.");
        return (long)minor;
    }
    public static PaymentMoney FromMinorUnits(long amount, string currency)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Stripe amounts cannot be negative.");
        var canonical = NormalizeCurrency(currency);
        var scale = (decimal)Math.Pow(10, Exponent(canonical));
        return new PaymentMoney(amount / scale, canonical);
    }
    private static string NormalizeCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Currency is required.", nameof(currency));
        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(static ch => ch is < 'A' or > 'Z')) throw new ArgumentException("Currency must be an uppercase ISO-like 3-letter code.", nameof(currency));
        return normalized;
    }
    private static int Exponent(string currency)
    {
        var c = NormalizeCurrency(currency);
        if (ZeroDecimalCurrencies.Contains(c)) return 0;
        if (TwoDecimalCurrencies.Contains(c)) return 2;
        throw new NotSupportedException($"Currency '{c}' is not configured for deterministic Stripe minor-unit conversion.");
    }
}
