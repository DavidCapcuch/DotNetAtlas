using Platform.SharedKernel.Errors;

namespace Payments.Domain.Errors;

/// <summary>
/// Business-expected failure error emitted when the payment gateway declines an authorize
/// or capture call (e.g., <c>insufficient_funds</c>, <c>card_declined</c>). The saga consumes
/// the translated external <c>PaymentFailedEvent</c> to drive its compensation branch.
/// Bug-class FSM violations use <see cref="Platform.SharedKernel.Exceptions.DataIntegrityException"/>,
/// not this type — see <c>docs/bc-design/error-taxonomy.md § 3.5</c>.
/// </summary>
/// <remarks>
/// Inherits <see cref="ConflictError"/> so the canonical
/// <c>Platform.Api.Extensions</c> dispatch maps it to 409 if it ever reaches the HTTP
/// boundary. Today the Authorize/Capture handlers intercept it via
/// <c>OfType&lt;GatewayDeclinedError&gt;()</c> and translate it into an outbox
/// <c>PaymentFailedEvent</c> — defense-in-depth in case a future endpoint surfaces the
/// raw Result. Modelled as a sealed class (not a record) because C# records cannot
/// inherit from non-record bases (CS8864).
/// </remarks>
/// <param name="reason">Human-readable reason the gateway provided.</param>
/// <param name="gatewayCode">Raw gateway code (e.g., <c>"insufficient_funds"</c>), if supplied.</param>
public sealed class GatewayDeclinedError(string reason, string? gatewayCode)
    : ConflictError(
        entityName: "Payment",
        message: gatewayCode is null
            ? $"Payment gateway declined: {reason}"
            : FormattableString.Invariant($"Payment gateway declined: {reason} ({gatewayCode})."),
        errorCode: "Payments.GatewayDeclined")
{
    public string Reason { get; } = reason;

    public string? GatewayCode { get; } = gatewayCode;
}
