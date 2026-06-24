using Dominatus.Actuators.Payments;

namespace Dominatus.Actuators.Payments.Stripe;

public static class StripePaymentStatusMapper
{
    public static PaymentStatus MapPaymentIntent(string? status) => status switch
    {
        "requires_payment_method" => PaymentStatus.Created,
        "requires_confirmation" => PaymentStatus.Pending,
        "requires_action" => PaymentStatus.RequiresAction,
        "processing" => PaymentStatus.Pending,
        "requires_capture" => PaymentStatus.Authorized,
        "canceled" => PaymentStatus.Canceled,
        "succeeded" => PaymentStatus.Succeeded,
        _ => PaymentStatus.Unknown
    };
    public static CheckoutSessionStatus MapCheckoutSession(string? sessionStatus, string? paymentStatus) => paymentStatus switch
    {
        "paid" => CheckoutSessionStatus.Completed,
        "no_payment_required" => CheckoutSessionStatus.Completed,
        _ => sessionStatus switch
        {
            "open" => CheckoutSessionStatus.Open,
            "complete" => CheckoutSessionStatus.Completed,
            "expired" => CheckoutSessionStatus.Expired,
            _ => CheckoutSessionStatus.Unknown
        }
    };
    public static RefundStatus MapRefund(string? status) => status switch
    {
        "pending" => RefundStatus.Pending,
        "requires_action" => RefundStatus.Pending,
        "succeeded" => RefundStatus.Succeeded,
        "failed" or "canceled" => RefundStatus.Failed,
        _ => RefundStatus.Unknown
    };
}
