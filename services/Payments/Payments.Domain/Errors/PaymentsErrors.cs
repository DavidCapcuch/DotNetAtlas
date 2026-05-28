using Platform.SharedKernel.Errors;

namespace Payments.Domain.Errors;

/// <summary>
/// Single source of truth for Payments BC user/business error factories.
/// Shapes are mirrored verbatim from <c>docs/bc-design/error-taxonomy.md § 3.5</c>.
/// Bug-class invariant violations (FSM transitions from terminal states, tampering with
/// <c>GatewayTransactionId</c>) throw <see cref="Platform.SharedKernel.Exceptions.DataIntegrityException"/>
/// and do NOT use this class.
/// </summary>
public static class PaymentsErrors
{
    public static NotFoundError PaymentNotFound(Guid paymentId) =>
        new("Payment", paymentId, "Payments.NotFound");

    public static ValidationError InvalidAmount() =>
        new("Amount", "Payment amount must be strictly positive.", "Payments.InvalidAmount");

    public static ValidationError InvalidPaymentMethod() =>
        new("PaymentMethodId", "Payment method token is empty or exceeds 64 characters.", "Payments.InvalidPaymentMethod");

    public static ServiceUnavailableError GatewayUnavailable() =>
        new("PaymentGateway", "Payment gateway is temporarily unavailable.", "Payments.GatewayUnavailable");
}
