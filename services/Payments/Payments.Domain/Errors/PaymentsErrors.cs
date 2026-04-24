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
    public static ValidationError PaymentNotFound(Guid paymentId) =>
        new("PaymentId", $"Payment '{paymentId}' does not exist.", "Payments.NotFound");

    public static ValidationError InvalidAmount() =>
        new("Amount", "Payment amount must be strictly positive.", "Payments.InvalidAmount");

    public static ValidationError InvalidPaymentMethod() =>
        new("PaymentMethodId", "Payment method token is empty or exceeds 64 characters.", "Payments.InvalidPaymentMethod");

    public static ValidationError GatewayUnavailable() =>
        new("Gateway", "Payment gateway is temporarily unavailable.", "Payments.GatewayUnavailable");
}
