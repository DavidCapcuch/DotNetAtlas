using Ardalis.SmartEnum;

namespace Payments.Domain.Transactions.ValueObjects;

/// <summary>
/// Classification of a terminal payment failure. Populated on the <see cref="FailureInfo"/>
/// value object when the aggregate transitions to <see cref="PaymentStatus.Failed"/>.
/// Mapping from raw gateway codes to <see cref="FailureReason"/> values lives in the
/// <c>StubPaymentGateway</c> adapter (M3) and is documented in
/// <c>docs/bc-design/payments.md § 3</c>.
/// </summary>
public sealed class FailureReason : SmartEnum<FailureReason>
{
    public static readonly FailureReason GatewayDeclined = new(nameof(GatewayDeclined), 0);
    public static readonly FailureReason GatewayTimeout = new(nameof(GatewayTimeout), 1);
    public static readonly FailureReason InsufficientFunds = new(nameof(InsufficientFunds), 2);
    public static readonly FailureReason FraudSuspected = new(nameof(FraudSuspected), 3);
    public static readonly FailureReason Cancelled = new(nameof(Cancelled), 4);
    public static readonly FailureReason Unknown = new(nameof(Unknown), 5);

    private FailureReason(string name, int value)
        : base(name, value)
    {
    }
}
